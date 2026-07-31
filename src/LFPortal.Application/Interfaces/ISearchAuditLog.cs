using LFPortal.Application.DTOs;

namespace LFPortal.Application.Interfaces;

/// <summary>
/// Records and queries portal-level search activity.
/// Backed by an in-memory rolling log (no external database required).
/// The log is populated whenever a user submits a search through LFPortal;
/// data does not persist across application restarts.
/// </summary>
/// <remarks>
/// This provides the data behind the dashboard's "Search Activity" line chart
/// and "Top Searched Queries" panel — the same source used by the original
/// GovSearch AI implementation. Laserfiche Repository API v1 does not expose
/// search history, so the portal maintains its own.
/// </remarks>
public interface ISearchAuditLog
{
    /// <summary>
    /// Records that a search query was submitted by the user.
    /// The query is stored with the current UTC timestamp.
    /// Thread-safe — may be called from multiple concurrent requests.
    /// </summary>
    /// <param name="query">The raw search query string as entered by the user.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task RecordSearchAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the search count for each of the last <paramref name="days"/> calendar
    /// days, ordered from oldest to newest (left to right on a chart X-axis).
    /// Days with no recorded searches are included with a count of zero.
    /// </summary>
    Task<IReadOnlyList<SearchActivityDayDto>> GetSearchesByDayAsync(
        int days = 7,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns the top <paramref name="n"/> most-frequently submitted queries,
    /// ordered by count descending.
    /// </summary>
    Task<IReadOnlyList<TopQueryDto>> GetTopQueriesAsync(
        int n = 5,
        CancellationToken cancellationToken = default);

    /// <summary>Total number of searches recorded since the application started.</summary>
    Task<int> GetTotalSearchCountAsync(CancellationToken cancellationToken = default);
}
