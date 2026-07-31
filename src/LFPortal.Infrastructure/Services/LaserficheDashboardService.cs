using System.Diagnostics;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Aggregates live Laserfiche data from multiple services into a single
/// <see cref="DashboardStatsDto"/> for the Analytics Dashboard page.
/// Always returns a populated DTO — errors are captured in
/// <see cref="DashboardStatsDto.ErrorMessage"/> and never propagated as exceptions.
/// </summary>
internal sealed class LaserficheDashboardService : ILaserficheDashboardService
{
    private readonly ILaserficheRepositoryService _repositoryService;
    private readonly ILaserficheEntryService      _entryService;
    private readonly ILaserficheSearchService     _searchService;
    private readonly ICredentialProvider          _credentialProvider;
    private readonly IRepositoryContext           _repositoryContext;
    private readonly ILogger<LaserficheDashboardService> _logger;

    public LaserficheDashboardService(
        ILaserficheRepositoryService repositoryService,
        ILaserficheEntryService      entryService,
        ILaserficheSearchService     searchService,
        ICredentialProvider          credentialProvider,
        IRepositoryContext           repositoryContext,
        ILogger<LaserficheDashboardService> logger)
    {
        _repositoryService  = repositoryService;
        _entryService       = entryService;
        _searchService      = searchService;
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
            // ── 1. Connectivity ─────────────────────────────────────────────
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

            // ── 2. Connected user (from credential store) ────────────────────
            string? connectedUser = null;
            try
            {
                var repoDesc = await _repositoryContext
                    .GetActiveRepositoryAsync(cancellationToken)
                    .ConfigureAwait(false);
                var creds = await _credentialProvider
                    .GetCredentialsAsync(repoDesc.Key, cancellationToken)
                    .ConfigureAwait(false);
                connectedUser = creds.Username;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Could not read username from credential store for dashboard. " +
                    "ConnectedUser will be null.");
            }

            // ── 3. Timed searches ────────────────────────────────────────────
            // Broad search: all entries ordered by most-recently modified.
            // TotalCount from this gives the real total across the whole repository.
            var sw = Stopwatch.StartNew();

            var recentSearch = await _searchService
                .AdvancedSearchAsync(
                    "{LF:Modify date}>=\"1900-01-01\"",
                    page: 1, pageSize: 100,
                    cancellationToken)
                .ConfigureAwait(false);
            var t1 = sw.Elapsed;

            // Type-specific count searches.
            // {LF:Document type}="Document" / "Folder" are valid Laserfiche search
            // expressions that return TotalCount for each class.
            sw.Restart();
            var docSearch = await _searchService
                .AdvancedSearchAsync(
                    "{LF:Document type}=\"Document\"",
                    page: 1, pageSize: 1,
                    cancellationToken)
                .ConfigureAwait(false);
            var t2 = sw.Elapsed;

            sw.Restart();
            var folderSearch = await _searchService
                .AdvancedSearchAsync(
                    "{LF:Document type}=\"Folder\"",
                    page: 1, pageSize: 1,
                    cancellationToken)
                .ConfigureAwait(false);
            var t3 = sw.Elapsed;
            sw.Stop();

            var avgResponseTime = TimeSpan.FromMilliseconds(
                (t1.TotalMilliseconds + t2.TotalMilliseconds + t3.TotalMilliseconds) / 3.0);

            // ── 4. Derive accurate type counts ───────────────────────────────
            //
            // The type-specific expressions rely on {LF:Document type} which is
            // supported by Laserfiche v10+ search engines. On some installations the
            // token may not match anything even when entries exist (e.g. custom
            // entry-class configuration). We detect this by comparing type counts
            // against the broad total and fall back to sample-based estimation when
            // the expressions produce 0 despite the broad search finding entries.
            var totalEntries  = recentSearch.TotalCount;
            int totalDocs, totalFolders;

            var typeExprWorked = docSearch.TotalCount > 0 || folderSearch.TotalCount > 0 || totalEntries == 0;

            if (typeExprWorked)
            {
                // {LF:Document type} expressions returned meaningful counts.
                totalDocs    = docSearch.TotalCount;
                totalFolders = folderSearch.TotalCount;
            }
            else
            {
                // Broad search found entries but type expressions returned 0.
                // Estimate using the entryType field from the sample items.
                _logger.LogWarning(
                    "Document-type search expressions returned 0 while broad search found " +
                    "{Total} entries. Falling back to sample-based type estimation.",
                    totalEntries);
                var sample   = recentSearch.Items;
                totalDocs    = sample.Count(r => r.EntryType == LFEntryType.Document);
                totalFolders = sample.Count(r => r.EntryType == LFEntryType.Folder);
            }

            // ── 5. Root-folder children → department list ────────────────────
            var deptList = new List<DepartmentStatDto>();
            try
            {
                var rootChildren = await _entryService
                    .GetEntryChildrenAsync(1, page: 1, pageSize: 50, cancellationToken)
                    .ConfigureAwait(false);

                deptList = rootChildren.Items
                    .Where(e => e.EntryType == LFEntryType.Folder)
                    .Select(e => new DepartmentStatDto { Name = e.Name, EntryId = e.Id })
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Could not retrieve root-folder children for the department list. " +
                    "Department stats will be derived from search-result path sampling.");
            }

            // ── 6. Convert recent search hits to LFEntry ─────────────────────
            var allRecent = recentSearch.Items.Select(r => new LFEntry
            {
                Id               = r.EntryId,
                Name             = r.Name,
                FullPath         = r.FullPath,
                FolderPath       = ExtractFolderPath(r.FullPath),
                EntryType        = r.EntryType,
                TemplateName     = r.TemplateName,
                Creator          = r.Creator,
                CreationTime     = r.CreationTime,
                LastModifiedTime = r.LastModifiedTime
            }).ToList();

            // ── 7. Department distribution by path sampling ──────────────────
            var pathGroups = allRecent
                .Where(e => !string.IsNullOrWhiteSpace(e.FullPath))
                .GroupBy(e => ExtractTopFolder(e.FullPath))
                .ToDictionary(g => g.Key, g => g.Count());

            List<DepartmentStatDto> deptStats;
            if (deptList.Count > 0)
            {
                deptStats = deptList
                    .Select(d => d with { DocumentCount = pathGroups.GetValueOrDefault(d.Name, 0) })
                    .OrderByDescending(d => d.DocumentCount)
                    .ToList();
            }
            else
            {
                deptStats = pathGroups
                    .Select(kv => new DepartmentStatDto { Name = kv.Key, DocumentCount = kv.Value })
                    .OrderByDescending(d => d.DocumentCount)
                    .Take(12)
                    .ToList();
            }

            // ── 8. Entry sub-lists ───────────────────────────────────────────
            var recentDocs = allRecent
                .Where(e => e.EntryType == LFEntryType.Document)
                .Take(10)
                .ToList();

            var recentlyIndexed = allRecent
                .Where(e => e.EntryType == LFEntryType.Document)
                .OrderByDescending(e => e.CreationTime ?? DateTimeOffset.MinValue)
                .Take(10)
                .ToList();

            var breakdown = new Dictionary<string, int>
            {
                ["Document"] = totalDocs,
                ["Folder"]   = totalFolders
            };

            return new DashboardStatsDto
            {
                IsConnected              = true,
                RepositoryId             = status.RepositoryId,
                RepositoryName           = status.RepositoryName,
                ServerVersion            = status.ServerVersion,
                ConnectedUser            = connectedUser,
                TotalEntries             = typeExprWorked ? totalDocs + totalFolders : totalEntries,
                TotalDocuments           = totalDocs,
                TotalFolders             = totalFolders,
                DepartmentCount          = deptList.Count > 0 ? deptList.Count : pathGroups.Count,
                AvgSearchResponseTime    = avgResponseTime,
                EntryTypeBreakdown       = breakdown,
                DocumentsByDepartment    = deptStats.AsReadOnly(),
                RecentEntries            = allRecent.Take(10).ToList().AsReadOnly(),
                RecentDocuments          = recentDocs.AsReadOnly(),
                RecentlyIndexedDocuments = recentlyIndexed.AsReadOnly(),
                LastCheckedAt            = DateTimeOffset.UtcNow
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

    // ── Path helpers ───────────────────────────────────────────────────────────

    private static string ExtractFolderPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
        var lastSep = fullPath.LastIndexOfAny(['\\', '/']);
        return lastSep > 0 ? fullPath[..lastSep] : string.Empty;
    }

    private static string ExtractTopFolder(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "Root";
        var trimmed = fullPath.TrimStart('\\', '/');
        var sep     = trimmed.IndexOfAny(['\\', '/']);
        return sep > 0 ? trimmed[..sep] : trimmed;
    }
}
