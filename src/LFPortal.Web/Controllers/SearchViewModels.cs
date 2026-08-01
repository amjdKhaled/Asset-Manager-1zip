using LFPortal.Domain.Common;
using LFPortal.Domain.Entities;

namespace LFPortal.Web.Controllers;

// ── Search page view models ───────────────────────────────────────────────────

/// <summary>Which search mode is active on the Search page.</summary>
public enum SearchMode
{
    /// <summary>Simple keyword/phrase search via <c>SimpleSearches</c>.</summary>
    Simple,

    /// <summary>Raw Laserfiche search expression via <c>Searches</c>.</summary>
    Advanced,

    /// <summary>Match all entries that have a specific template applied.</summary>
    Template,

    /// <summary>Match entries where a specific metadata field contains a value.</summary>
    Field
}

/// <summary>
/// View model for <c>Search/Index</c>. Carries both the form inputs (preserved on
/// round-trip) and the result set produced by the most recent search.
/// </summary>
public sealed record SearchViewModel
{
    // ── Form inputs ───────────────────────────────────────────────────────────

    /// <summary>Active search tab.</summary>
    public SearchMode Mode { get; init; } = SearchMode.Simple;

    /// <summary>Keyword or Laserfiche expression (Simple and Advanced modes).</summary>
    public string Query { get; init; } = string.Empty;

    /// <summary>Template name to filter by (Template mode).</summary>
    public string TemplateName { get; init; } = string.Empty;

    /// <summary>Field name for field-value search (Field mode).</summary>
    public string FieldName { get; init; } = string.Empty;

    /// <summary>Field value to match (Field mode).</summary>
    public string FieldValue { get; init; } = string.Empty;

    /// <summary>Current 1-based page number.</summary>
    public int Page { get; init; } = 1;

    // ── Results ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Populated after a search has been submitted.
    /// <c>null</c> means the page has not been submitted yet.
    /// </summary>
    public PagedResult<LFSearchResult>? Results { get; init; }

    /// <summary><c>true</c> when a search has been submitted (even if zero results).</summary>
    public bool HasSearched { get; init; }

    // ── Error states ──────────────────────────────────────────────────────────

    /// <summary>Non-null when the search failed with a user-facing message.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary><c>true</c> when the error is an authentication failure.</summary>
    public bool IsAuthError { get; init; }

    /// <summary><c>true</c> when the search timed out waiting for Laserfiche.</summary>
    public bool IsTimeout { get; init; }

    // ── Dropdown data ─────────────────────────────────────────────────────────

    /// <summary>Template names for the Template mode dropdown. Empty if unavailable.</summary>
    public IReadOnlyList<string> AvailableTemplates { get; init; } = [];

    /// <summary>Field names for the Field mode dropdown. Empty if unavailable.</summary>
    public IReadOnlyList<string> AvailableFields { get; init; } = [];

    // ── Constants ─────────────────────────────────────────────────────────────

    /// <summary>Results per page. Kept consistent across all search modes.</summary>
    public const int DefaultPageSize = 25;
}
