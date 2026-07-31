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
    /// Submits a search to the Laserfiche API, polls until complete, and retrieves
    /// the paged results. Implements the full long-operation lifecycle.
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

        using var submitResponse = await client
            .PostAsJsonAsync(searchUrl, requestBody, JsonOptions.Default, cancellationToken)
            .ConfigureAwait(false);

        if (!submitResponse.IsSuccessStatusCode)
        {
            var errBody = await submitResponse.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            _logger.LogWarning(
                "Search submission failed for query '{Query}': HTTP {StatusCode}. Body: {Body}",
                displayQuery,
                (int)submitResponse.StatusCode,
                errBody);

            throw new LaserficheException(
                $"Search failed with HTTP {(int)submitResponse.StatusCode}. " +
                $"Query: {displayQuery}",
                (int)submitResponse.StatusCode);
        }

        var submitBody = await submitResponse.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        var taskResult = JsonSerializer.Deserialize<LongOperationResponse>(submitBody, JsonOptions.Default);

        // Step 2: If results are inline (synchronous completion), return them directly
        if (taskResult?.Status?.Equals("Completed", StringComparison.OrdinalIgnoreCase) == true
            || string.IsNullOrWhiteSpace(taskResult?.OperationToken))
        {
            return await FetchSearchResultsAsync(
                client, repo.RepositoryId, taskResult?.OperationToken ?? string.Empty,
                displayQuery, page, pageSize, cancellationToken)
                .ConfigureAwait(false);
        }

        // Step 3: Poll for completion
        var token = taskResult.OperationToken;
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
