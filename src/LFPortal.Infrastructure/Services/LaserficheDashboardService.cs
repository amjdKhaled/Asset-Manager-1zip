using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Aggregates live Laserfiche data from multiple services into a single
/// <see cref="DashboardStatsDto"/> for the Dashboard page.
/// Always returns a populated DTO — errors are captured in
/// <see cref="DashboardStatsDto.ErrorMessage"/> and never propagated as exceptions.
/// </summary>
internal sealed class LaserficheDashboardService : ILaserficheDashboardService
{
    private readonly ILaserficheRepositoryService _repositoryService;
    private readonly ILaserficheEntryService _entryService;
    private readonly ILaserficheSearchService _searchService;
    private readonly ILogger<LaserficheDashboardService> _logger;

    /// <summary>Initialises the dashboard service with all required dependencies.</summary>
    public LaserficheDashboardService(
        ILaserficheRepositoryService repositoryService,
        ILaserficheEntryService entryService,
        ILaserficheSearchService searchService,
        ILogger<LaserficheDashboardService> logger)
    {
        _repositoryService = repositoryService;
        _entryService = entryService;
        _searchService = searchService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<DashboardStatsDto> GetDashboardStatsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 1. Verify connectivity and get repository info
            var status = await _repositoryService
                .TestConnectionAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!status.IsConnected)
            {
                return new DashboardStatsDto
                {
                    IsConnected    = false,
                    ErrorMessage   = status.ErrorMessage,
                    LastCheckedAt  = status.CheckedAt
                };
            }

            // 2. Fetch recent entries (last 10 modified, across all entry types)
            var recentSearch = await _searchService
                .AdvancedSearchAsync(
                    "{LF:Modify date}>=\"1900-01-01\"",
                    page: 1,
                    pageSize: 10,
                    cancellationToken)
                .ConfigureAwait(false);

            // 3. Count documents via search
            var docSearch = await _searchService
                .AdvancedSearchAsync("{LF:Document type}=\"Document\"", 1, 1, cancellationToken)
                .ConfigureAwait(false);

            // 4. Count folders via search
            var folderSearch = await _searchService
                .AdvancedSearchAsync("{LF:Document type}=\"Folder\"", 1, 1, cancellationToken)
                .ConfigureAwait(false);

            var totalDocs    = docSearch.TotalCount;
            var totalFolders = folderSearch.TotalCount;
            var totalEntries = totalDocs + totalFolders;

            // Build recent entries from search results (converted to LFEntry for the DTO)
            var recentEntries = recentSearch.Items
                .Select(r => new LFEntry
                {
                    Id               = r.EntryId,
                    Name             = r.Name,
                    FullPath         = r.FullPath,
                    EntryType        = r.EntryType,
                    TemplateName     = r.TemplateName,
                    Creator          = r.Creator,
                    CreationTime     = r.CreationTime,
                    LastModifiedTime = r.LastModifiedTime
                })
                .ToList();

            var breakdown = new Dictionary<string, int>
            {
                ["Document"] = totalDocs,
                ["Folder"]   = totalFolders
            };

            return new DashboardStatsDto
            {
                IsConnected         = true,
                RepositoryName      = status.RepositoryName,
                ServerVersion       = status.ServerVersion,
                TotalEntries        = totalEntries,
                TotalDocuments      = totalDocs,
                TotalFolders        = totalFolders,
                EntryTypeBreakdown  = breakdown,
                RecentEntries       = recentEntries.AsReadOnly(),
                LastCheckedAt       = DateTimeOffset.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dashboard stats aggregation failed.");

            return new DashboardStatsDto
            {
                IsConnected   = false,
                ErrorMessage  = $"Failed to retrieve dashboard data: {ex.Message}",
                LastCheckedAt = DateTimeOffset.UtcNow
            };
        }
    }
}
