using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Common;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Implements Laserfiche search operations using the Repository API v2 search endpoints.
/// The Laserfiche search API uses an asynchronous long-operation pattern: a search is
/// submitted, then polled until complete, and finally results are retrieved.
/// </summary>
/// <remarks>
/// <para>
/// Polling interval is 500 ms with a maximum wait of <see cref="MaxPollDurationSeconds"/>
/// seconds before a <see cref="TimeoutException"/> is thrown.
/// </para>
/// <para>
/// All search modes build a Laserfiche search expression and delegate to
/// <see cref="ExecuteSearchAsync"/> which handles the full long-operation lifecycle.
/// </para>
/// </remarks>
internal sealed class LaserficheSearchService : ILaserficheSearchService
{
    private const int MaxPollDurationSeconds = 30;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRepositoryContext _repositoryContext;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly ILogger<LaserficheSearchService> _logger;

    /// <summary>Initialises the service with all required dependencies.</summary>
    public LaserficheSearchService(
        IHttpClientFactory httpClientFactory,
        IRepositoryContext repositoryContext,
        ILaserficheApiAdapter adapter,
        ILogger<LaserficheSearchService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repositoryContext = repositoryContext;
        _adapter = adapter;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<PagedResult<LFSearchResult>> SimpleSearchAsync(
        string query,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ExecuteSearchAsync(
            query,
            SearchType.Simple,
            $"{{LF:Basic}}=\"{EscapeSearchTerm(query)}\"",
            page,
            pageSize,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<LFSearchResult>> AdvancedSearchAsync(
        string searchExpression,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ExecuteSearchAsync(
            searchExpression,
            SearchType.Advanced,
            searchExpression,
            page,
            pageSize,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<LFSearchResult>> SearchByTemplateAsync(
        string templateName,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ExecuteSearchAsync(
            templateName,
            SearchType.Advanced,
            $"{{LF:Template}}=\"{EscapeSearchTerm(templateName)}\"",
            page,
            pageSize,
            cancellationToken);

    /// <inheritdoc />
    public Task<PagedResult<LFSearchResult>> SearchByFieldAsync(
        string fieldName,
        string fieldValue,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default) =>
        ExecuteSearchAsync(
            $"{fieldName}={fieldValue}",
            SearchType.Advanced,
            $"{{{EscapeSearchTerm(fieldName)}}}=\"{EscapeSearchTerm(fieldValue)}\"",
            page,
            pageSize,
            cancellationToken);

    // ──────────────────────────── Core search orchestration ───────────────

    /// <summary>
    /// Submits a search to the Laserfiche API and returns paged results.
    /// <para>
    /// <b>SimpleSearches</b> (v1 synchronous): the API returns an OData collection
    /// directly in the submit response — no polling required.
    /// </para>
    /// <para>
    /// <b>Searches</b> (v1 async long-operation): the API returns an operationToken;
    /// the service polls <c>GET /Tasks/{token}</c> until status is Completed, then
    /// fetches results from <c>GET /SearchResults/{token}</c>.
    /// </para>
    /// </summary>
    private async Task<PagedResult<LFSearchResult>> ExecuteSearchAsync(
        string displayQuery,
        SearchType searchType,
        string expression,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

        // Step 1: Submit the search
        var searchUrl = _adapter.BuildSearchUrl(repo.RepositoryId, searchType);
        var requestBody = new { searchCommand = expression };

        _logger.LogInformation(
            "Search submit → POST {Url} | query: {Query}",
            searchUrl,
            displayQuery);

        using var submitResponse = await client
            .PostAsJsonAsync(searchUrl, requestBody, JsonOptions.Default, cancellationToken)
            .ConfigureAwait(false);

        var submitBody = await submitResponse.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Search submit response HTTP {Status} | RAW: {Body}",
            (int)submitResponse.StatusCode,
            submitBody);

        if (!submitResponse.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Search submission failed for query '{Query}': HTTP {StatusCode}. Body: {Body}",
                displayQuery,
                (int)submitResponse.StatusCode,
                submitBody);

            throw new LaserficheException(
                $"Search failed with HTTP {(int)submitResponse.StatusCode}. " +
                $"Query: {displayQuery}",
                (int)submitResponse.StatusCode);
        }

        // Step 2a: SimpleSearches (v1) — OData collection returned inline, no polling.
        // Detect by the presence of a "value" array at the root.
        using var submitDoc = JsonDocument.Parse(submitBody);
        if (submitDoc.RootElement.TryGetProperty("value", out _))
        {
            _logger.LogInformation(
                "Search returned inline OData collection for query '{Query}'.",
                displayQuery);

            return ParseInlineResults(submitBody, displayQuery, page, pageSize);
        }

        // Step 2b: Searches (v1 async) — operationToken in response body.
        var taskResult = JsonSerializer.Deserialize<LongOperationResponse>(submitBody, JsonOptions.Default);

        if (taskResult?.Status?.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true)
        {
            // Completed synchronously — fetch results immediately.
            return await FetchSearchResultsAsync(
                client, repo.RepositoryId, taskResult.OperationToken ?? string.Empty,
                displayQuery, page, pageSize, cancellationToken)
                .ConfigureAwait(false);
        }

        if (string.IsNullOrWhiteSpace(taskResult?.OperationToken))
        {
            _logger.LogWarning(
                "Search for '{Query}' returned neither 'value' array nor operationToken. " +
                "Raw body: {Body}", displayQuery, submitBody);

            return PagedResult<LFSearchResult>.Empty;
        }

        // Step 3: Poll for completion
        var token = taskResult.OperationToken!;
        var statusUrl = _adapter.BuildTaskStatusUrl(repo.RepositoryId, token);
        var deadline = DateTimeOffset.UtcNow.AddSeconds(MaxPollDurationSeconds);

        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);

            using var statusResponse = await client
                .GetAsync(statusUrl, cancellationToken)
                .ConfigureAwait(false);

            if (!statusResponse.IsSuccessStatusCode) continue;

            var statusBody = await statusResponse.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var status = JsonSerializer.Deserialize<LongOperationResponse>(statusBody, JsonOptions.Default);

            if (status?.Status?.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true)
            {
                return await FetchSearchResultsAsync(
                    client, repo.RepositoryId, token,
                    displayQuery, page, pageSize, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (status?.Status?.Equals("Failed", StringComparison.OrdinalIgnoreCase) == true)
            {
                throw new LaserficheException(
                    $"Laserfiche search operation failed for query: {displayQuery}",
                    500);
            }
        }

        _logger.LogWarning(
            "Search timed out after {Seconds}s for query '{Query}'.",
            MaxPollDurationSeconds,
            displayQuery);

        throw new TimeoutException(
            $"Laserfiche search timed out after {MaxPollDurationSeconds} seconds. " +
            "Try a more specific query.");
    }

    /// <summary>
    /// Parses an inline OData collection returned synchronously by
    /// <c>POST /SimpleSearches</c> (Laserfiche v1).
    /// </summary>
    private PagedResult<LFSearchResult> ParseInlineResults(
        string body,
        string displayQuery,
        int page,
        int pageSize)
    {
        _logger.LogInformation(
            "Parsing inline search results for query '{Query}'.", displayQuery);

        try
        {
            var resultList = JsonSerializer.Deserialize<ODataCountList<SearchResultResource>>(
                body, JsonOptions.Default);

            var allItems = resultList?.Value.Select(r => new LFSearchResult
            {
                EntryId          = r.Id,
                Name             = r.Name,
                FullPath         = r.FullPath,
                EntryType        = ParseEntryType(r.EntryType),
                TemplateName     = r.TemplateName,
                Creator          = r.Creator,
                CreationTime     = r.CreationTime,
                LastModifiedTime = r.LastModifiedTime
            }).ToList() ?? [];

            // Apply client-side pagination since SimpleSearches returns all results at once.
            var skip  = (page - 1) * pageSize;
            var items = allItems.Skip(skip).Take(pageSize).ToList();

            return new PagedResult<LFSearchResult>
            {
                Items      = items.AsReadOnly(),
                TotalCount = resultList?.Count > 0 ? resultList.Count : allItems.Count,
                PageNumber = page,
                PageSize   = pageSize
            };
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex,
                "Failed to parse inline search results for query '{Query}'. Body: {Body}",
                displayQuery, body);

            return PagedResult<LFSearchResult>.Empty;
        }
    }

    /// <summary>
    /// Retrieves a paged set of results from a completed search operation.
    /// </summary>
    private async Task<PagedResult<LFSearchResult>> FetchSearchResultsAsync(
        HttpClient client,
        string repositoryId,
        string operationToken,
        string displayQuery,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(operationToken))
        {
            return PagedResult<LFSearchResult>.Empty;
        }

        var skip = (page - 1) * pageSize;
        var resultsUrl = $"{_adapter.BuildSearchResultsUrl(repositoryId, operationToken)}" +
                         $"?$top={pageSize}&$skip={skip}&$count=true";

        using var response = await client
            .GetAsync(resultsUrl, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Failed to retrieve search results for operation {Token}: HTTP {StatusCode}.",
                operationToken,
                (int)response.StatusCode);

            return PagedResult<LFSearchResult>.Empty;
        }

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        var resultList = JsonSerializer.Deserialize<ODataCountList<SearchResultResource>>(
            body, JsonOptions.Default);

        var items = resultList?.Value.Select(r => new LFSearchResult
        {
            EntryId          = r.Id,
            Name             = r.Name,
            FullPath         = r.FullPath,
            EntryType        = ParseEntryType(r.EntryType),
            TemplateName     = r.TemplateName,
            Creator          = r.Creator,
            CreationTime     = r.CreationTime,
            LastModifiedTime = r.LastModifiedTime
        }).ToList() ?? [];

        return new PagedResult<LFSearchResult>
        {
            Items      = items.AsReadOnly(),
            TotalCount = resultList?.Count ?? items.Count,
            PageNumber = page,
            PageSize   = pageSize
        };
    }

    /// <summary>Escapes special characters in a search term for use in LF expressions.</summary>
    private static string EscapeSearchTerm(string term) =>
        term.Replace("\"", "\\\"").Replace("\\", "\\\\");

    private static LFEntryType ParseEntryType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "document"     => LFEntryType.Document,
        "folder"       => LFEntryType.Folder,
        "shortcut"     => LFEntryType.Shortcut,
        "recordseries" => LFEntryType.RecordSeries,
        _              => LFEntryType.Unknown
    };

    // ──────────────────────────── Response models ──────────────────────────

    private sealed record LongOperationResponse
    {
        [JsonPropertyName("operationToken")]
        public string? OperationToken { get; init; }

        [JsonPropertyName("status")]
        public string? Status { get; init; }

        [JsonPropertyName("percentComplete")]
        public int PercentComplete { get; init; }

        [JsonPropertyName("errors")]
        public List<string> Errors { get; init; } = [];
    }

    private sealed record ODataCountList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];

        [JsonPropertyName("@odata.count")]
        public int Count { get; init; }
    }

    private sealed record SearchResultResource
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("fullPath")]
        public string FullPath { get; init; } = string.Empty;

        [JsonPropertyName("entryType")]
        public string? EntryType { get; init; }

        [JsonPropertyName("templateName")]
        public string? TemplateName { get; init; }

        [JsonPropertyName("creator")]
        public string? Creator { get; init; }

        [JsonPropertyName("creationTime")]
        public DateTimeOffset? CreationTime { get; init; }

        [JsonPropertyName("lastModifiedTime")]
        public DateTimeOffset? LastModifiedTime { get; init; }
    }
}
