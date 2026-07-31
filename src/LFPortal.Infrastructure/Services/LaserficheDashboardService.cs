using System.Collections.Concurrent;
using System.Diagnostics;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
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
    private readonly ILogger<LaserficheDashboardService> _logger;

    public LaserficheDashboardService(
        ILaserficheRepositoryService   repositoryService,
        ILaserficheEntryService         entryService,
        ILaserficheTemplateService      templateService,
        ISearchAuditLog                 auditLog,
        ICredentialProvider             credentialProvider,
        IRepositoryContext              repositoryContext,
        ILogger<LaserficheDashboardService> logger)
    {
        _repositoryService  = repositoryService;
        _entryService       = entryService;
        _templateService    = templateService;
        _auditLog           = auditLog;
        _credentialProvider = credentialProvider;
        _repositoryContext  = repositoryContext;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<DashboardStatsDto> GetDashboardStatsAsync(
        CancellationToken cancellationToken = default)
    {
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
            string? connectedUser = null;
            string? serverUrl     = null;
            try
            {
                var repoDesc = await _repositoryContext
                    .GetActiveRepositoryAsync(cancellationToken)
                    .ConfigureAwait(false);
                var creds    = await _credentialProvider
                    .GetCredentialsAsync(repoDesc.Key, cancellationToken)
                    .ConfigureAwait(false);
                connectedUser = creds.Username;
                serverUrl     = repoDesc.ServerUrl;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read username from credential store.");
            }

            // ── 3. Parallel: root children + template definitions ────────────
            var tokenStart = Stopwatch.GetTimestamp();

            var (rootChildren, templateDefs) = await FetchRootAndTemplatesAsync(cancellationToken)
                .ConfigureAwait(false);

            var tokenDurationMs = Stopwatch.GetElapsedTime(tokenStart).TotalMilliseconds;

            // Separate root-level documents from root-level folders
            var rootDocEntries    = rootChildren.Where(e => e.EntryType == LFEntryType.Document).ToList();
            var rootFolderEntries = rootChildren.Where(e => e.EntryType == LFEntryType.Folder).ToList();

            // ── 4. Recursive folder scan ─────────────────────────────────────
            var scanStart = Stopwatch.GetTimestamp();

            var rootFolderResults = await ScanRootFoldersAsync(rootFolderEntries, cancellationToken)
                .ConfigureAwait(false);

            var scanDurationMs = (long)Stopwatch.GetElapsedTime(scanStart).TotalMilliseconds;

            _logger.LogInformation(
                "Dashboard scan complete — {TotalFolders} root folders scanned in {ScanMs}ms.",
                rootFolderEntries.Count, scanDurationMs);

            // ── 5. Aggregate totals ──────────────────────────────────────────
            var totalDocuments =
                rootDocEntries.Count +
                rootFolderResults.Sum(r => r.Documents);

            var totalFolders =
                rootFolderEntries.Count +
                rootFolderResults.Sum(r => r.Folders);

            // Template counts: merge root-level docs + all sub-folder results
            var globalTemplates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var doc in rootDocEntries.Where(d => !string.IsNullOrWhiteSpace(d.TemplateName)))
                globalTemplates[doc.TemplateName!] = globalTemplates.GetValueOrDefault(doc.TemplateName!) + 1;

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

            var allRecentDocs =
                rootDocMapped.Concat(rootFolderResults.SelectMany(r => r.AllDocs))
                .OrderByDescending(d => d.CreationTime ?? DateTimeOffset.MinValue)
                .Take(DocCap)
                .ToList()
                .AsReadOnly();

            var allModifiedDocs =
                rootDocMapped.Concat(rootFolderResults.SelectMany(r => r.AllDocs))
                .OrderByDescending(d => d.LastModifiedTime ?? d.CreationTime ?? DateTimeOffset.MinValue)
                .Take(DocCap)
                .ToList()
                .AsReadOnly();

            var allDocs =
                rootDocMapped.Concat(rootFolderResults.SelectMany(r => r.AllDocs))
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

            return new DashboardStatsDto
            {
                IsConnected              = true,
                RepositoryId             = status.RepositoryId,
                RepositoryName           = status.RepositoryName,
                ServerVersion            = status.ServerVersion,
                ServerUrl                = serverUrl,
                ConnectedUser            = connectedUser,
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
        _logger.LogInformation("Fetching root entry (ID=1) children and template definitions in parallel.");
        var rootTask     = SafeGetAllFolderChildrenAsync(1, ct);
        var templateTask = _templateService.GetTemplateDefinitionsAsync(ct);
        await Task.WhenAll(rootTask, templateTask).ConfigureAwait(false);

        var root  = await rootTask;
        var tmpls = await templateTask;

        _logger.LogInformation(
            "Root children: {RootCount} entries (docs={Docs}, folders={Folders}, other={Other}). Templates defined: {TmplCount}.",
            root.Count,
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

    private async Task<IReadOnlyList<ScanResult>> ScanRootFoldersAsync(
        IEnumerable<LFEntry> rootFolders,
        CancellationToken    ct)
    {
        var folderList = rootFolders.ToList();
        _logger.LogInformation("Starting recursive scan of {Count} root-level folders.", folderList.Count);

        var tasks = folderList.Select(async folder =>
        {
            try
            {
                var result = await ScanFolderAsync(folder.Id, folder.Name, new ConcurrentDictionary<int, byte>(), ct)
                    .ConfigureAwait(false);
                return result with { Name = folder.Name };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Root folder scan failed for folder {FolderId} '{Name}'.", folder.Id, folder.Name);
                return new ScanResult(folder.Name, 0, 0, [], []);
            }
        });

        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        _logger.LogInformation(
            "Root folder scan complete: {Docs} docs, {Folders} folders across {Count} root folders.",
            results.Sum(r => r.Documents),
            results.Sum(r => r.Folders),
            results.Length);

        return results;
    }

    // ── Private: recursive folder scanner ────────────────────────────────

    private async Task<ScanResult> ScanFolderAsync(
        int                           folderId,
        string                        folderName,
        ConcurrentDictionary<int, byte> visited,
        CancellationToken             ct)
    {
        if (!visited.TryAdd(folderId, 0))
        {
            _logger.LogDebug("Cycle detected — skipping already-visited folder {FolderId}.", folderId);
            return new ScanResult(folderName, 0, 0, [], []);
        }

        IReadOnlyList<LFEntry> children;
        try
        {
            children = await _entryService
                .GetAllFolderChildrenAsync(folderId, ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Cannot list children of folder {FolderId} '{Name}'.", folderId, folderName);
            return new ScanResult(folderName, 0, 0, [], []);
        }

        var docEntries    = children.Where(e => e.EntryType == LFEntryType.Document).ToList();
        var subFolderEntries = children.Where(e => e.EntryType == LFEntryType.Folder).ToList();

        // Collect template counts from this folder's documents
        var localTmpl = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var doc in docEntries.Where(d => !string.IsNullOrWhiteSpace(d.TemplateName)))
            localTmpl[doc.TemplateName!] = localTmpl.GetValueOrDefault(doc.TemplateName!) + 1;

        // Recurse into sub-folders in parallel
        var subTasks = subFolderEntries.Select(f =>
            ScanFolderAsync(f.Id, f.Name, visited, ct));

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

    // ── Private: audit log data ───────────────────────────────────────────

    private async Task<(IReadOnlyList<SearchActivityDayDto>, IReadOnlyList<TopQueryDto>, int)>
        FetchAuditDataAsync(CancellationToken ct)
    {
        var actTask  = _auditLog.GetSearchesByDayAsync(7, ct);
        var topTask  = _auditLog.GetTopQueriesAsync(5, ct);
        var cntTask  = _auditLog.GetTotalSearchCountAsync(ct);
        await Task.WhenAll(actTask, topTask, cntTask).ConfigureAwait(false);
        return (await actTask, await topTask, await cntTask);
    }

    // ── Private scan result record ────────────────────────────────────────

    private sealed record ScanResult(
        string                     Name,
        int                        Documents,
        int                        Folders,
        Dictionary<string, int>    TemplateCounts,
        List<LFEntry>              AllDocs);
}
