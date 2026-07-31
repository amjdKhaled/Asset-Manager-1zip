using LFPortal.Domain.Entities;

namespace LFPortal.Application.DTOs;

/// <summary>
/// Aggregated live statistics returned by <see cref="Interfaces.ILaserficheDashboardService"/>.
/// All values are sourced exclusively from the Laserfiche Repository API v1.
/// No values are fabricated, cached locally, or sourced from a local database.
/// </summary>
public sealed record DashboardStatsDto
{
    // ── Connectivity ───────────────────────────────────────────────────────

    /// <summary><c>true</c> when all data was successfully retrieved from a reachable Laserfiche server.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Error description when <see cref="IsConnected"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; init; }

    // ── Repository identity ────────────────────────────────────────────────

    /// <summary>Repository identifier used in API requests, e.g. <c>Documents</c>.</summary>
    public string? RepositoryId { get; init; }

    /// <summary>Human-readable display name of the active repository.</summary>
    public string? RepositoryName { get; init; }

    /// <summary>Laserfiche Server version string, e.g. <c>11.0.2310.12345</c>.</summary>
    public string? ServerVersion { get; init; }

    // ── Entry counts ───────────────────────────────────────────────────────

    /// <summary>Total number of entries (documents + folders + other types) in the repository.</summary>
    public int TotalEntries { get; init; }

    /// <summary>Total number of document entries in the repository.</summary>
    public int TotalDocuments { get; init; }

    /// <summary>Total number of folder entries in the repository.</summary>
    public int TotalFolders { get; init; }

    /// <summary>Number of top-level folders identified as organisational departments.</summary>
    public int DepartmentCount { get; init; }

    /// <summary>
    /// Laserfiche username of the currently authenticated service account.
    /// Sourced from the credential store at dashboard load time.
    /// Null when credentials cannot be retrieved (e.g. during a cold-start before Settings are saved).
    /// </summary>
    public string? ConnectedUser { get; init; }

    // ── Performance ────────────────────────────────────────────────────────

    /// <summary>
    /// Average elapsed time of the search API calls made during this dashboard refresh.
    /// Null when no search calls completed successfully.
    /// </summary>
    public TimeSpan? AvgSearchResponseTime { get; init; }

    // ── Breakdowns ─────────────────────────────────────────────────────────

    /// <summary>
    /// Count of entries keyed by entry-type name, e.g.
    /// <c>{ "Document": 1_200, "Folder": 340 }</c>.
    /// </summary>
    public IReadOnlyDictionary<string, int> EntryTypeBreakdown { get; init; }
        = new Dictionary<string, int>();

    /// <summary>
    /// Approximate document/entry distribution across top-level department folders.
    /// Derived by grouping the recent-entry sample by each entry's first path segment.
    /// </summary>
    public IReadOnlyList<DepartmentStatDto> DocumentsByDepartment { get; init; } = [];

    // ── Recent entries ─────────────────────────────────────────────────────

    /// <summary>Up to 10 most recently modified entries across all entry types.</summary>
    public IReadOnlyList<LFEntry> RecentEntries { get; init; } = [];

    /// <summary>Up to 10 most recently modified document entries.</summary>
    public IReadOnlyList<LFEntry> RecentDocuments { get; init; } = [];

    /// <summary>Up to 10 most recently created document entries (proxy for "recently indexed").</summary>
    public IReadOnlyList<LFEntry> RecentlyIndexedDocuments { get; init; } = [];

    // ── Timing ─────────────────────────────────────────────────────────────

    /// <summary>UTC timestamp of when this snapshot was fetched from Laserfiche.</summary>
    public DateTimeOffset? LastCheckedAt { get; init; }
}
