using LFPortal.Domain.Entities;

namespace LFPortal.Application.DTOs;

/// <summary>
/// Aggregated statistics returned by <see cref="Interfaces.ILaserficheDashboardService"/>.
/// All values are sourced exclusively from the live Laserfiche Repository API.
/// No values are calculated locally or sourced from a local cache or database.
/// </summary>
public sealed record DashboardStatsDto
{
    /// <summary><c>true</c> when all data was retrieved from a reachable Laserfiche server.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Display name of the active repository. Null when not connected.</summary>
    public string? RepositoryName { get; init; }

    /// <summary>Laserfiche Server version string. Null when not connected.</summary>
    public string? ServerVersion { get; init; }

    /// <summary>Total number of entries (documents + folders + shortcuts) in the repository.</summary>
    public int TotalEntries { get; init; }

    /// <summary>Total number of folder entries in the repository.</summary>
    public int TotalFolders { get; init; }

    /// <summary>Total number of document entries in the repository.</summary>
    public int TotalDocuments { get; init; }

    /// <summary>
    /// Breakdown of entry counts by entry type name, e.g.
    /// <c>{ "Document": 1200, "Folder": 340, "Shortcut": 5 }</c>.
    /// </summary>
    public IReadOnlyDictionary<string, int> EntryTypeBreakdown { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Up to 10 most recently modified entries across the repository.</summary>
    public IReadOnlyList<LFEntry> RecentEntries { get; init; } = [];

    /// <summary>UTC timestamp of when this data was fetched.</summary>
    public DateTimeOffset? LastCheckedAt { get; init; }

    /// <summary>Error description when <see cref="IsConnected"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; init; }
}
