using System.Collections.Concurrent;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// In-memory rolling audit log for portal search activity.
/// Backed by a <see cref="ConcurrentQueue{T}"/> capped at <see cref="MaxCapacity"/> entries.
/// Data is not persisted across application restarts — this mirrors the original
/// GovSearch AI implementation which used a <c>MemStorage</c> in-memory store.
/// </summary>
/// <remarks>
/// Registered as a singleton so the log accumulates across the lifetime of the
/// application process. Thread-safe for concurrent read and write.
/// </remarks>
internal sealed class InMemorySearchAuditLog : ISearchAuditLog
{
    private const int MaxCapacity = 10_000;

    private readonly ConcurrentQueue<SearchAuditEntry> _entries = new();

    /// <inheritdoc />
    public Task RecordSearchAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Task.CompletedTask;

        _entries.Enqueue(new SearchAuditEntry(query.Trim(), DateTimeOffset.UtcNow));

        // Trim to cap — dequeue oldest entries when over limit
        while (_entries.Count > MaxCapacity)
        {
            _entries.TryDequeue(out _);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SearchActivityDayDto>> GetSearchesByDayAsync(
        int days = 7,
        CancellationToken cancellationToken = default)
    {
        var now   = DateTimeOffset.UtcNow.Date;
        var cutoff = now.AddDays(-(days - 1));

        // Initialise all buckets with 0
        var buckets = new Dictionary<DateOnly, int>(days);
        for (var i = 0; i < days; i++)
        {
            buckets[DateOnly.FromDateTime(cutoff.AddDays(i))] = 0;
        }

        foreach (var entry in _entries)
        {
            var day = DateOnly.FromDateTime(entry.SearchedAt.LocalDateTime);
            if (buckets.ContainsKey(day))
                buckets[day]++;
        }

        var result = buckets
            .OrderBy(kv => kv.Key)
            .Select(kv => new SearchActivityDayDto
            {
                Date  = kv.Key.ToString("yyyy-MM-dd"),
                Label = kv.Key.ToString("MMM d"),
                Count = kv.Value
            })
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<SearchActivityDayDto>>(result);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<TopQueryDto>> GetTopQueriesAsync(
        int n = 5,
        CancellationToken cancellationToken = default)
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in _entries)
        {
            var key = entry.Query.ToLowerInvariant();
            counts[key] = (counts.TryGetValue(key, out var c) ? c : 0) + 1;
        }

        var top = counts
            .OrderByDescending(kv => kv.Value)
            .Take(n)
            .Select(kv => new TopQueryDto { Query = kv.Key, Count = kv.Value })
            .ToList()
            .AsReadOnly();

        return Task.FromResult<IReadOnlyList<TopQueryDto>>(top);
    }

    /// <inheritdoc />
    public Task<int> GetTotalSearchCountAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_entries.Count);

    // ── Private record ─────────────────────────────────────────────────────

    private sealed record SearchAuditEntry(string Query, DateTimeOffset SearchedAt);
}
