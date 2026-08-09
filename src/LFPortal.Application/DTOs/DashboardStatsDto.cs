using LFPortal.Domain.Entities;

namespace LFPortal.Application.DTOs;

/// <summary>
/// Aggregated live statistics returned by <see cref="Interfaces.ILaserficheDashboardService"/>.
/// Mirrors the shape of the original GovSearch AI dashboard stats endpoint exactly.
///
/// Data comes from two sources:
///   1. Laserfiche Repository API v1 (recursive folder scan + template definitions)
///   2. Portal-side in-memory search audit log (search activity history, top queries)
/// </summary>
public sealed record DashboardStatsDto
{
    // ── Connectivity ───────────────────────────────────────────────────────

    /// <summary><c>true</c> when all LF data was successfully retrieved.</summary>
    public bool IsConnected { get; init; }

    /// <summary>Alias for <see cref="IsConnected"/> — matches original React prop name.</summary>
    public bool IsLive => IsConnected;

    /// <summary>Error description when <see cref="IsConnected"/> is <c>false</c>.</summary>
    public string? ErrorMessage { get; init; }

    // ── Repository identity ────────────────────────────────────────────────

    /// <summary>Repository identifier used in API requests, e.g. <c>Documents</c>.</summary>
    public string? RepositoryId { get; init; }

    /// <summary>Human-readable display name of the active repository.</summary>
    public string? RepositoryName { get; init; }

    /// <summary>Server version string extracted from HTTP response headers, or <c>"Laserfiche API v1"</c>.</summary>
    public string? ServerVersion { get; init; }

    /// <summary>Base server URL, e.g. <c>https://lf-server.corp.local</c>.</summary>
    public string? ServerUrl { get; init; }

    /// <summary>Laserfiche username of the currently authenticated service account.</summary>
    public string? ConnectedUser { get; init; }

    /// <summary>
    /// Describes the credential type used for all Laserfiche API calls in this session.
    /// <list type="bullet">
    ///   <item><c>"FallbackCredentials"</c> — stored DPAPI/env service-account credentials (current default).</item>
    ///   <item><c>"UserSession"</c> — a real per-user Laserfiche Bearer token obtained via LFDS OAuth
    ///         (requires LFDS configuration; currently dormant).</item>
    /// </list>
    /// This value is displayed in the System Health panel so operators can see the effective
    /// identity without having to read log files.
    /// </summary>
    public string AuthenticationMode { get; init; } = "FallbackCredentials";

    // ── Entry counts (from recursive folder scan) ──────────────────────────

    /// <summary>Total number of document entries found by the recursive scan.</summary>
    public int TotalDocuments { get; init; }

    /// <summary>Total number of folder entries found by the recursive scan.</summary>
    public int TotalFolders { get; init; }

    /// <summary>Number of template definitions registered in the repository.</summary>
    public int TotalTemplates { get; init; }

    /// <summary>Number of documents that have a non-empty template assigned.</summary>
    public int DocsWithTemplate { get; init; }

    /// <summary>Number of documents with no template: <c>TotalDocuments − DocsWithTemplate</c>.</summary>
    public int DocsWithoutTemplate { get; init; }

    // ── Template stats (from recursive scan) ──────────────────────────────

    /// <summary>
    /// Document counts keyed by template name, sorted descending by count.
    /// Matches <c>templateStats</c> from the original dashboard.
    /// </summary>
    public IReadOnlyList<TemplateStatDto> TemplateStats { get; init; } = [];

    // ── Folder distribution (root-level) ──────────────────────────────────

    /// <summary>
    /// Document + sub-folder counts for each root-level folder.
    /// Provides data for the "Documents by Folder" bar chart.
    /// Matches <c>rootFolders</c> from the original dashboard.
    /// </summary>
    public IReadOnlyList<RootFolderStatDto> RootFolders { get; init; } = [];

    // ── Document lists (from recursive scan) ──────────────────────────────

    /// <summary>
    /// Up to 120 most recently <em>created</em> documents, sorted by creation time desc.
    /// Matches <c>recentDocs</c> from the original dashboard.
    /// </summary>
    public IReadOnlyList<LFEntry> RecentDocs { get; init; } = [];

    /// <summary>
    /// Up to 120 most recently <em>modified</em> documents, sorted by last-modified desc.
    /// Matches <c>modifiedDocs</c> from the original dashboard.
    /// </summary>
    public IReadOnlyList<LFEntry> ModifiedDocs { get; init; } = [];

    /// <summary>
    /// All documents discovered during the scan (up to DOC_CAP).
    /// Used to compute the "Documents by User Activity" widget.
    /// Matches <c>allDocs</c> from the original dashboard.
    /// </summary>
    public IReadOnlyList<LFEntry> AllDocs { get; init; } = [];

    // ── Search activity (from portal in-memory audit log) ─────────────────

    /// <summary>
    /// Per-day search counts for the last 7 days, oldest-first.
    /// Populated by <see cref="Interfaces.ISearchAuditLog"/> — not from Laserfiche API.
    /// </summary>
    public IReadOnlyList<SearchActivityDayDto> SearchActivityByDay { get; init; } = [];

    /// <summary>
    /// Top 5 most-frequently submitted search queries, from the portal audit log.
    /// </summary>
    public IReadOnlyList<TopQueryDto> TopSearchedQueries { get; init; } = [];

    /// <summary>Total number of searches recorded since the portal started.</summary>
    public int TotalSearches { get; init; }

    // ── Health / timing ────────────────────────────────────────────────────

    /// <summary>
    /// Wall-clock milliseconds spent traversing the folder tree during this refresh.
    /// Matches <c>health.scanDurationMs</c> from the original dashboard.
    /// </summary>
    public long ScanDurationMs { get; init; }

    /// <summary>UTC timestamp of when this snapshot was fetched.</summary>
    public DateTimeOffset? LastCheckedAt { get; init; }

    // ── Legacy / compat fields kept from previous implementation ──────────

    /// <summary>Total entries (documents + folders). Kept for backward compat.</summary>
    public int TotalEntries => TotalDocuments + TotalFolders;

    /// <summary>Average response time of timed API calls. Kept for backward compat.</summary>
    public TimeSpan? AvgSearchResponseTime { get; init; }

    /// <summary>Entry-type breakdown dict. Kept for backward compat.</summary>
    public IReadOnlyDictionary<string, int> EntryTypeBreakdown { get; init; }
        = new Dictionary<string, int>();

    /// <summary>Department-based distribution (alias of RootFolders for backward compat).</summary>
    public IReadOnlyList<DepartmentStatDto> DocumentsByDepartment { get; init; } = [];

    /// <summary>Department count. Kept for backward compat.</summary>
    public int DepartmentCount { get; init; }

    /// <summary>Recently created docs list (alias). Kept for backward compat.</summary>
    public IReadOnlyList<LFEntry> RecentDocuments { get; init; } = [];

    /// <summary>Recently indexed docs list (alias). Kept for backward compat.</summary>
    public IReadOnlyList<LFEntry> RecentlyIndexedDocuments { get; init; } = [];

    /// <summary>Recent entries (all types). Kept for backward compat.</summary>
    public IReadOnlyList<LFEntry> RecentEntries { get; init; } = [];
}
