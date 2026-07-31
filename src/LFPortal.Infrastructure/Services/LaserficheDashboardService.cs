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
    private readonly ILaserficheEntryService _entryService;
    private readonly ILaserficheSearchService _searchService;
    private readonly ILogger<LaserficheDashboardService> _logger;

    public LaserficheDashboardService(
        ILaserficheRepositoryService repositoryService,
        ILaserficheEntryService entryService,
        ILaserficheSearchService searchService,
        ILogger<LaserficheDashboardService> logger)
    {
        _repositoryService = repositoryService;
        _entryService      = entryService;
        _searchService     = searchService;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<DashboardStatsDto> GetDashboardStatsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Verify connectivity and obtain repository identity.
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

            // 2. Timed searches — record elapsed time for the "Avg Search Response Time" card.
            var sw = Stopwatch.StartNew();

            // Recent entries (up to 100) — source of several derived stats.
            var recentSearch = await _searchService
                .AdvancedSearchAsync("{LF:Modify date}>=\"1900-01-01\"", 1, 100, cancellationToken)
                .ConfigureAwait(false);
            var t1 = sw.Elapsed;

            sw.Restart();
            var docSearch = await _searchService
                .AdvancedSearchAsync("{LF:Document type}=\"Document\"", 1, 1, cancellationToken)
                .ConfigureAwait(false);
            var t2 = sw.Elapsed;

            sw.Restart();
            var folderSearch = await _searchService
                .AdvancedSearchAsync("{LF:Document type}=\"Folder\"", 1, 1, cancellationToken)
                .ConfigureAwait(false);
            var t3 = sw.Elapsed;
            sw.Stop();

            var avgResponseTime = TimeSpan.FromMilliseconds(
                (t1.TotalMilliseconds + t2.TotalMilliseconds + t3.TotalMilliseconds) / 3.0);

            // 3. Root-level folder children → department list.
            //    Entry ID 1 is the repository root in Laserfiche.
            //    Wrapped in its own try-catch because some server configurations may
            //    restrict access to the root entry.
            var deptList = new List<DepartmentStatDto>();
            try
            {
                var rootChildren = await _entryService
                    .GetEntryChildrenAsync(1, 1, 50, cancellationToken)
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

            // 4. Convert recent search hits to LFEntry for the various entry lists.
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

            // 5. Approximate department distribution by bucketing each entry's
            //    first path segment (e.g. "\HR\2024\Report.pdf" → "HR").
            var pathGroups = allRecent
                .Where(e => !string.IsNullOrWhiteSpace(e.FullPath))
                .GroupBy(e => ExtractTopFolder(e.FullPath))
                .ToDictionary(g => g.Key, g => g.Count());

            List<DepartmentStatDto> deptStats;
            if (deptList.Count > 0)
            {
                // Annotate API-fetched departments with sample-based counts.
                deptStats = deptList
                    .Select(d => d with { DocumentCount = pathGroups.GetValueOrDefault(d.Name, 0) })
                    .OrderByDescending(d => d.DocumentCount)
                    .ToList();
            }
            else
            {
                // Fall back to path-derived department names.
                deptStats = pathGroups
                    .Select(kv => new DepartmentStatDto { Name = kv.Key, DocumentCount = kv.Value })
                    .OrderByDescending(d => d.DocumentCount)
                    .Take(12)
                    .ToList();
            }

            var totalDocs    = docSearch.TotalCount;
            var totalFolders = folderSearch.TotalCount;

            var recentDocs = allRecent
                .Where(e => e.EntryType == LFEntryType.Document)
                .Take(10)
                .ToList();

            // "Recently indexed" ≈ most recently created documents.
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
                TotalEntries             = totalDocs + totalFolders,
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

    /// <summary>
    /// Returns the folder path portion of a full entry path, e.g.
    /// <c>\HR\2024\Report.pdf</c> → <c>\HR\2024</c>.
    /// </summary>
    private static string ExtractFolderPath(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return string.Empty;
        var lastSep = fullPath.LastIndexOfAny(['\\', '/']);
        return lastSep > 0 ? fullPath[..lastSep] : string.Empty;
    }

    /// <summary>
    /// Returns the first path segment (top-level folder / department name) of a full
    /// entry path, e.g. <c>\HR\2024\Report.pdf</c> → <c>HR</c>.
    /// </summary>
    private static string ExtractTopFolder(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return "Root";
        var trimmed = fullPath.TrimStart('\\', '/');
        var sep     = trimmed.IndexOfAny(['\\', '/']);
        return sep > 0 ? trimmed[..sep] : trimmed;
    }
}
