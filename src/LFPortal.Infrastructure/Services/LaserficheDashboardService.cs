using System.Collections.Concurrent;
using System.Diagnostics;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using Microsoft.AspNetCore.Http;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Aggregates live Laserfiche repository statistics into a <see cref="DashboardStatsDto"/>
/// by performing a recursive folder-tree scan — the same approach used by the original
/// GovSearch AI Node.js backend.
///
/// Data sources:
///   • Laserfiche Repository API v1 — folder children, template definitions
///   • Portal in-memory audit log   — search activity history, top queries
/// </summary>
internal sealed class LaserficheDashboardService : ILaserficheDashboardService
{
    /// <summary>
    /// Maximum number of documents collected across the entire scan.
    /// Matches the <c>DOC_CAP = 120</c> constant in the original implementation.
    /// </summary>
    private const int DocCap = 120;

    private readonly ILaserficheRepositoryService   _repositoryService;
    private readonly ILaserficheEntryService         _entryService;
    private readonly ILaserficheTemplateService      _templateService;
    private readonly ISearchAuditLog                 _auditLog;
    private readonly ICredentialProvider             _credentialProvider;
    private readonly IRepositoryContext              _repositoryContext;
    private readonly IHttpContextAccessor            _httpContextAccessor;
    private readonly ILogger<LaserficheDashboardService> _logger;

    public LaserficheDashboardService(
        ILaserficheRepositoryService   repositoryService,
        ILaserficheEntryService         entryService,
        ILaserficheTemplateService      templateService,
        ISearchAuditLog                 auditLog,
        ICredentialProvider             credentialProvider,
        IRepositoryContext              repositoryContext,
        IHttpContextAccessor            httpContextAccessor,
        ILogger<LaserficheDashboardService> logger)
    {
        _repositoryService  = repositoryService;
        _entryService       = entryService;
        _templateService    = templateService;
        _auditLog           = auditLog;
        _credentialProvider = credentialProvider;
        _repositoryContext  = repositoryContext;
        _httpContextAccessor = httpContextAccessor;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<DashboardStatsDto> GetDashboardStatsAsync(
        CancellationToken cancellationToken = default)
    {
        var totalStart = Stopwatch.GetTimestamp();
        try
        {
            // ── 1. Verify connectivity ───────────────────────────────────────
            var status = await _repositoryService
                .TestConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!status.IsConnected)
            {
                return new DashboardStatsDto
                {
                    IsConnected   = false,
                    ErrorMessage  = status.ErrorMessage,
                    LastCheckedAt = status.CheckedAt
                };
            }

            // ── 2. Connected user ────────────────────────────────────────────
            var principal = _httpContextAccessor.HttpContext?.User;
            var authMethod = principal?.FindFirst(ClaimTypes.AuthenticationMethod)?.Value;
            var isUserSession = principal?.Identity?.IsAuthenticated == true &&
                (string.Equals(authMethod, "LFDS", StringComparison.Ordinal) ||
                 string.Equals(authMethod, "RepositoryPassword", StringComparison.Ordinal));
            var authenticationMode = isUserSession ? authMethod! : "FallbackCredentials";

            string? connectedUser = principal?.Identity?.Name;
            string? serverUrl     = null;
            try
            {
                var repoDesc = await _repositoryContext
                    .GetActiveRepositoryAsync(cancellationToken)
                    .ConfigureAwait(false);
                serverUrl     = repoDesc.ServerUrl;

                // Interactive API calls already use the per-user bearer token. Never read
                // or display the configured fallback account while such a session exists.
                if (!isUserSession)
                {
                    var creds = await _credentialProvider
                        .GetCredentialsAsync(repoDesc.Key, cancellationToken)
                        .ConfigureAwait(false);
                    connectedUser = creds.Username;
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read username from credential store.");
            }

            // ── 3. Parallel: root children + template definitions ────────────
            var tokenStart = Stopwatch.GetTimestamp();

            var (rootChildren, templateDefs) = await FetchRootAndTemplatesAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Dashboard scan starting. RepositoryId={RepositoryId}; AuthenticationMode={AuthenticationMode}; RootChildrenCount={RootChildrenCount}.",
                status.RepositoryId, authenticationMode, rootChildren.Count);
            _logger.LogInformation(
                "Dashboard statistics loaded for Username={Username}; RepositoryId={RepositoryId}.",
                connectedUser ?? "(not exposed by token)", status.RepositoryId);

            var tokenDurationMs = Stopwatch.GetElapsedTime(tokenStart).TotalMilliseconds;

            // Separate root-level documents from root-level folders
            var rootDocEntries    = rootChildren.Where(e => e.EntryType == LFEntryType.Document).ToList();
            var rootFolderEntries = rootChildren.Where(e => e.EntryType == LFEntryType.Folder).ToList();
            var rootOtherEntries  = rootChildren.Where(e => e.EntryType == LFEntryType.Unknown).ToList();

            // ── Pipeline diagnostic checkpoint 1 ────────────────────────────
            _logger.LogInformation(
                "DASHBOARD PIPELINE — Root children: total={Total} | folders={Folders} | documents={Docs} | unknown={Other}",
                rootChildren.Count, rootFolderEntries.Count, rootDocEntries.Count, rootOtherEntries.Count);

            if (rootChildren.Count == 0)
            {
                _logger.LogWarning(
                    "DASHBOARD DIAGNOSTIC — RepositoryId={RepositoryId}; API user returned 0 root children. " +
                    "If Laserfiche Web Client shows entries, the API identity may have fewer permissions or the bearer token may belong to a different user/repository.",
                    status.RepositoryId);
            }

            // ── 4. Recursive folder scan ─────────────────────────────────────
            var scanStart = Stopwatch.GetTimestamp();

            var rootFolderResults = await ScanRootFoldersAsync(
                    rootFolderEntries,
                    (id, ct) => _entryService.GetAllFolderChildrenAsync(id, ct),
                    _logger,
                    cancellationToken)
                .ConfigureAwait(false);

            var scanDurationMs = (long)Stopwatch.GetElapsedTime(scanStart).TotalMilliseconds;

            _logger.LogInformation(
                "DASHBOARD PIPELINE — Recursive scan complete in {ScanMs}ms | root folders={RootFolders} | sub-folders={SubFolders} | sub-documents={SubDocs}",
                scanDurationMs, rootFolderEntries.Count,
                rootFolderResults.Sum(r => r.Folders),
                rootFolderResults.Sum(r => r.Documents));

            // ── 5. Aggregate totals ──────────────────────────────────────────
            var totalDocuments =
                rootDocEntries.Count +
                rootFolderResults.Sum(r => r.Documents);

            var totalFolders =
                rootFolderEntries.Count +
                rootFolderResults.Sum(r => r.Folders);

            // Template counts: merge root-level docs + all sub-folder results
            var globalTemplates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in rootDocEntries.Where(HasTemplate))
            {
                var templateKey = GetTemplateKey(doc);
                globalTemplates[templateKey] = globalTemplates.GetValueOrDefault(templateKey) + 1;
            }

            foreach (var r in rootFolderResults)
            {
                foreach (var (tmpl, cnt) in r.TemplateCounts)
                    globalTemplates[tmpl] = globalTemplates.GetValueOrDefault(tmpl) + cnt;
            }

            var templateStats = globalTemplates
                .Select(kv => new TemplateStatDto { Name = kv.Key, Count = kv.Value })
                .OrderByDescending(t => t.Count)
                .ToList()
                .AsReadOnly();

            var docsWithTemplate    = templateStats.Sum(t => t.Count);
            var docsWithoutTemplate = Math.Max(0, totalDocuments - docsWithTemplate);

            // ── Pipeline diagnostic checkpoint 2 ────────────────────────────
            _logger.LogInformation(
                "DASHBOARD PIPELINE — Final counts: totalFolders={TotalFolders} | totalDocuments={TotalDocs} | " +
                "docsWithTemplate={WithTemplate} | docsWithoutTemplate={WithoutTemplate} | templateNames={Templates}",
                totalFolders, totalDocuments, docsWithTemplate, docsWithoutTemplate,
                string.Join(", ", templateStats.Take(5).Select(t => $"{t.Name}({t.Count})")));

            // Root-folder distribution (for bar chart)
            var rootFolderStats = rootFolderResults
                .Select(r => new RootFolderStatDto
                {
                    Name      = r.Name,
                    Documents = r.Documents,
                    Folders   = r.Folders
                })
                .ToList()
                .AsReadOnly();

            // ── 6. Build sorted document lists ───────────────────────────────
            var rootDocMapped = rootDocEntries;  // already LFEntry

            // The Repository API commonly sets LastModifiedTime on initial creation.
            // For dashboard activity, treat that initial timestamp as creation only;
            // a modification is counted only when it happened after creation.
            var allDocs =
                rootDocMapped.Concat(rootFolderResults.SelectMany(r => r.AllDocs))
                .Select(NormalizeActivityEntry)
                .ToList()
                .AsReadOnly();

            var allRecentDocs =
                allDocs
                .OrderByDescending(d => d.CreationTime ?? DateTimeOffset.MinValue)
                .Take(DocCap)
                .ToList()
                .AsReadOnly();

            var allModifiedDocs =
                allDocs
                .Where(d => d.LastModifiedTime.HasValue)
                .OrderByDescending(d => d.LastModifiedTime ?? DateTimeOffset.MinValue)
                .Take(DocCap)
                .ToList()
                .AsReadOnly();

            // ── 7. Search audit log ──────────────────────────────────────────
            var (activityByDay, topQueries, totalSearches) = await FetchAuditDataAsync(cancellationToken)
                .ConfigureAwait(false);

            // ── 8. Build DTO ─────────────────────────────────────────────────
            var entryBreakdown = new Dictionary<string, int>
            {
                ["Document"] = totalDocuments,
                ["Folder"]   = totalFolders
            };

            // DepartmentStatDto compatibility (map rootFolderStats)
            var deptStats = rootFolderStats
                .Select(r => new DepartmentStatDto { Name = r.Name, DocumentCount = r.Documents })
                .ToList()
                .AsReadOnly();

            var totalLoadMs = (long)Stopwatch.GetElapsedTime(totalStart).TotalMilliseconds;

            _logger.LogInformation(
                "DASHBOARD LOAD — total={TotalMs}ms | token+root={TokenMs}ms | scan={ScanMs}ms | " +
                "docs={TotalDocs} | folders={TotalFolders} | templates={Templates} | " +
                "authMode={AuthenticationMode}",
                totalLoadMs,
                (long)tokenDurationMs,
                scanDurationMs,
                totalDocuments,
                totalFolders,
                templateDefs.Count,
                authenticationMode);

            return new DashboardStatsDto
            {
                IsConnected              = true,
                RepositoryId             = status.RepositoryId,
                RepositoryName           = status.RepositoryName,
                ServerVersion            = status.ServerVersion,
                ServerUrl                = serverUrl,
                ConnectedUser            = connectedUser,
                AuthenticationMode       = authenticationMode,
                TotalDocuments           = totalDocuments,
                TotalFolders             = totalFolders,
                TotalTemplates           = templateDefs.Count,
                DocsWithTemplate         = docsWithTemplate,
                DocsWithoutTemplate      = docsWithoutTemplate,
                TemplateStats            = templateStats,
                RootFolders              = rootFolderStats,
                RecentDocs               = allRecentDocs,
                ModifiedDocs             = allModifiedDocs,
                AllDocs                  = allDocs,
                SearchActivityByDay      = activityByDay,
                TopSearchedQueries       = topQueries,
                TotalSearches            = totalSearches,
                ScanDurationMs           = scanDurationMs,
                LastCheckedAt            = DateTimeOffset.UtcNow,
                // Legacy compat fields
                EntryTypeBreakdown       = entryBreakdown,
                DepartmentCount          = rootFolderEntries.Count,
                DocumentsByDepartment    = deptStats,
                RecentDocuments          = allRecentDocs,
                RecentlyIndexedDocuments = allRecentDocs,
                RecentEntries            = allDocs.Take(10).ToList().AsReadOnly()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard statistics aggregation failed.");
            return new DashboardStatsDto
            {
                IsConnected   = false,
                ErrorMessage  = $"Failed to retrieve dashboard data: {ex.Message}",
                LastCheckedAt = DateTimeOffset.UtcNow
            };
        }
    }

    // ── Private: fetch root children + templates in parallel ──────────────

    private async Task<(IReadOnlyList<LFEntry> rootChildren, IReadOnlyList<Domain.Entities.LFTemplateDefinition> templateDefs)>
        FetchRootAndTemplatesAsync(CancellationToken ct)
    {
        const int rootId = 1;
        _logger.LogInformation("Using root entry ID={RootId}. Fetching children and template definitions in parallel.", rootId);

        var rootTask     = SafeGetAllFolderChildrenAsync(rootId, ct);
        var templateTask = _templateService.GetTemplateDefinitionsAsync(ct);
        await Task.WhenAll(rootTask, templateTask).ConfigureAwait(false);

        var root  = await rootTask;
        var tmpls = await templateTask;

        _logger.LogInformation(
            "Root children (ID={RootId}): {RootCount} entries (docs={Docs}, folders={Folders}, other={Other}). Templates defined: {TmplCount}.",
            rootId, root.Count,
            root.Count(e => e.EntryType == Domain.Entities.LFEntryType.Document),
            root.Count(e => e.EntryType == Domain.Entities.LFEntryType.Folder),
            root.Count(e => e.EntryType == Domain.Entities.LFEntryType.Unknown),
            tmpls.Count);

        return (root, tmpls);
    }

    private async Task<IReadOnlyList<LFEntry>> SafeGetAllFolderChildrenAsync(int entryId, CancellationToken ct)
    {
        try
        {
            return await _entryService.GetAllFolderChildrenAsync(entryId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SafeGetAllFolderChildrenAsync: unhandled exception for entry {EntryId}.", entryId);
            return [];
        }
    }

    // ── Private: parallel scan of each root-level folder ─────────────────

    internal static async Task<IReadOnlyList<ScanResult>> ScanRootFoldersAsync(
        IEnumerable<LFEntry> rootFolders,
        Func<int, CancellationToken, Task<IReadOnlyList<LFEntry>>> loadChildren,
        ILogger logger,
        CancellationToken    ct)
    {
        var folderList = rootFolders.ToList();
        logger.LogInformation("Starting recursive scan of {Count} root-level folders.", folderList.Count);

        var tasks = folderList.Select(async folder =>
        {
            try
            {
                var result = await ScanFolderAsync(folder.Id, folder.Name, new ConcurrentDictionary<int, byte>(), loadChildren, logger, ct)
                    .ConfigureAwait(false);
                return result with { Name = folder.Name };
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Root folder scan failed for folder {FolderId} '{Name}'.", folder.Id, folder.Name);
                return new ScanResult(folder.Name, 0, 0, [], []);
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        logger.LogInformation(
            "Root folder scan complete: {Docs} docs, {Folders} folders across {Count} root folders.",
            results.Sum(r => r.Documents),
            results.Sum(r => r.Folders),
            results.Length);

        return results;
    }

    // ── Private: recursive folder scanner ────────────────────────────────

    private static async Task<ScanResult> ScanFolderAsync(
        int                           folderId,
        string                        folderName,
        ConcurrentDictionary<int, byte> visited,
        Func<int, CancellationToken, Task<IReadOnlyList<LFEntry>>> loadChildren,
        ILogger logger,
        CancellationToken             ct)
    {
        if (!visited.TryAdd(folderId, 0))
        {
            logger.LogDebug("Cycle detected — skipping already-visited folder {FolderId}.", folderId);
            return new ScanResult(folderName, 0, 0, [], []);
        }

        IReadOnlyList<LFEntry> children;
        try
        {
            children = await loadChildren(folderId, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Cannot list children of folder {FolderId} '{Name}'.", folderId, folderName);
            return new ScanResult(folderName, 0, 0, [], []);
        }

        var docEntries    = children.Where(e => e.EntryType == LFEntryType.Document).ToList();
        var subFolderEntries = children.Where(e => e.EntryType == LFEntryType.Folder).ToList();

        // Collect template counts from this folder's documents
        var localTmpl = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docEntries.Where(HasTemplate))
        {
            var templateKey = GetTemplateKey(doc);
            localTmpl[templateKey] = localTmpl.GetValueOrDefault(templateKey) + 1;
        }

        logger.LogInformation(
            "Dashboard folder scanned. FolderId={FolderId}; FolderName={FolderName}; DirectDocuments={Documents}; DirectFolders={Folders}.",
            folderId, folderName, docEntries.Count, subFolderEntries.Count);

        // Recurse into sub-folders in parallel
        var subTasks = subFolderEntries.Select(f =>
            ScanFolderAsync(f.Id, f.Name, visited, loadChildren, logger, ct));

        var subResults = await Task.WhenAll(subTasks).ConfigureAwait(false);

        // Merge template counts from sub-folders
        foreach (var sub in subResults)
        {
            foreach (var (tmpl, cnt) in sub.TemplateCounts)
                localTmpl[tmpl] = localTmpl.GetValueOrDefault(tmpl) + cnt;
        }

        var documents = docEntries.Count + subResults.Sum(r => r.Documents);
        var folders   = subFolderEntries.Count + subResults.Sum(r => r.Folders);

        // Collect document entries from sub-folders
        var allSubDocs = subResults.SelectMany(r => r.AllDocs).ToList();
        var allDocs    = docEntries.Concat(allSubDocs).ToList();

        return new ScanResult(folderName, documents, folders, localTmpl, allDocs);
    }

    private static bool HasTemplate(LFEntry entry) =>
        entry.EntryType == LFEntryType.Document &&
        (entry.TemplateId is > 0 || !string.IsNullOrWhiteSpace(entry.TemplateName));

    private static string GetTemplateKey(LFEntry entry) =>
        !string.IsNullOrWhiteSpace(entry.TemplateName)
            ? entry.TemplateName!
            : $"Template #{entry.TemplateId}";

    /// <summary>
    /// Returns true only for an actual post-creation modification. Laserfiche may
    /// populate LastModifiedTime during creation, which must not make a new document
    /// appear in both Created and Modified on the activity chart.
    /// </summary>
    private static bool HasMeaningfulModification(LFEntry entry)
    {
        if (!entry.LastModifiedTime.HasValue)
            return false;

        if (!entry.CreationTime.HasValue)
            return true;

        return entry.LastModifiedTime.Value > entry.CreationTime.Value.AddSeconds(1);
    }

    private static LFEntry NormalizeActivityEntry(LFEntry entry) =>
        HasMeaningfulModification(entry)
            ? entry
            : entry with { LastModifiedTime = null };

    // ── Private: audit log data ───────────────────────────────────────────

    private async Task<(IReadOnlyList<SearchActivityDayDto>, IReadOnlyList<TopQueryDto>, int)>
        FetchAuditDataAsync(CancellationToken ct)
    {
        // Repository-scoped: dashboard statistics must only reflect search
        // activity in the CURRENT session's repository.
        var repo = await _repositoryContext.GetActiveRepositoryAsync(ct).ConfigureAwait(false);

        var actTask  = _auditLog.GetSearchesByDayAsync(repo.RepositoryId, 7, ct);
        var topTask  = _auditLog.GetTopQueriesAsync(repo.RepositoryId, 5, ct);
        var cntTask  = _auditLog.GetTotalSearchCountAsync(repo.RepositoryId, ct);
        await Task.WhenAll(actTask, topTask, cntTask).ConfigureAwait(false);
        return (await actTask, await topTask, await cntTask);
    }

    // ── Private scan result record ────────────────────────────────────────

    internal sealed record ScanResult(
        string                     Name,
        int                        Documents,
        int                        Folders,
        Dictionary<string, int>    TemplateCounts,
        List<LFEntry>              AllDocs);
}