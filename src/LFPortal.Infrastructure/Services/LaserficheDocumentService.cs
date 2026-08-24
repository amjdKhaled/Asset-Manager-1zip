using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Implements document retrieval operations against the active Laserfiche Repository API.
/// Collection endpoints are read to completion; failed source requests are surfaced rather
/// than converted to empty data.
/// </summary>
internal sealed class LaserficheDocumentService : ILaserficheDocumentService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRepositoryContext _repositoryContext;
    private readonly ILaserficheEntryService _entryService;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly ILogger<LaserficheDocumentService> _logger;

    public LaserficheDocumentService(
        IHttpClientFactory httpClientFactory,
        IRepositoryContext repositoryContext,
        ILaserficheEntryService entryService,
        ILaserficheApiAdapter adapter,
        ILogger<LaserficheDocumentService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repositoryContext = repositoryContext;
        _entryService = entryService;
        _adapter = adapter;
        _logger = logger;
    }

    public async Task<IReadOnlyList<LFDocumentPage>> GetDocumentPagesAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var firstUrl = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, EntryResource.Pages);
        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

        var pages = new List<PageResource>();
        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? nextUrl = firstUrl;
        var apiPage = 0;

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedUrls.Add(nextUrl))
            {
                throw new LaserficheException(
                    $"Document pages pagination repeated a nextLink for entry {entryId}: {nextUrl}",
                    500);
            }

            apiPage++;
            using var response = await client.GetAsync(nextUrl, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new LaserficheException(
                    $"Document pages request failed for entry {entryId}: HTTP {(int)response.StatusCode}. " +
                    $"URL: {nextUrl}. Body: {body}",
                    (int)response.StatusCode);
            }

            var parsed = ParsePage(body);
            pages.AddRange(parsed.Items);
            nextUrl = ResolveNextLink(nextUrl, parsed.NextLink);

            _logger.LogInformation(
                "Document pages. EntryId={EntryId}; ApiPage={ApiPage}; Items={Items}; RunningTotal={Total}; HasNext={HasNext}.",
                entryId, apiPage, parsed.Items.Count, pages.Count, nextUrl is not null);
        }

        return pages
            .Where(p => p.PageNumber > 0)
            .GroupBy(p => p.PageNumber)
            .Select(g => g.Last())
            .OrderBy(p => p.PageNumber)
            .Select(p => new LFDocumentPage
            {
                PageNumber = p.PageNumber,
                Width = p.Width,
                Height = p.Height,
                MimeType = p.MimeType
            })
            .ToList()
            .AsReadOnly();
    }

    public async Task<LaserficheEdocStream> StreamEdocAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, EntryResource.Edoc);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new LaserficheException(
                $"Electronic document request failed for entry {entryId}: HTTP {statusCode}. Body: {body}",
                statusCode);
        }

        try
        {
            var contentStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            var contentType = response.Content.Headers.ContentType?.MediaType
                ?? "application/octet-stream";
            var contentDisposition = response.Content.Headers.ContentDisposition?.ToString();
            var fileName = GetFileName(response.Content.Headers.ContentDisposition);
            var extension = GetExtension(fileName, contentType);

            return new LaserficheEdocStream(
                contentStream,
                contentType,
                contentDisposition,
                fileName,
                extension,
                response.Content.Headers.ContentLength,
                response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public async Task<LaserficheEdocStream> GetPageImageAsync(
        int entryId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        if (pageNumber < 1)
            throw new ArgumentOutOfRangeException(nameof(pageNumber), "Page number must be at least 1.");

        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildPageImageUrl(repo.RepositoryId, entryId, pageNumber);

        var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new LaserficheException(
                $"Page image not available for entry {entryId} page {pageNumber}: " +
                $"HTTP {statusCode}. Body: {body}",
                statusCode);
        }

        try
        {
            var contentStream = await response.Content
                .ReadAsStreamAsync(cancellationToken)
                .ConfigureAwait(false);

            // Do not invent an image type when the server omits Content-Type.
            var contentType = response.Content.Headers.ContentType?.MediaType
                ?? "application/octet-stream";

            return new LaserficheEdocStream(
                contentStream,
                contentType,
                contentDisposition: response.Content.Headers.ContentDisposition?.ToString(),
                fileName: GetFileName(response.Content.Headers.ContentDisposition),
                extension: GetExtension(GetFileName(response.Content.Headers.ContentDisposition), contentType),
                contentLength: response.Content.Headers.ContentLength,
                owner: response);
        }
        catch
        {
            response.Dispose();
            throw;
        }
    }

    public Task<LFEntry> GetDocumentMetadataAsync(
        int entryId,
        CancellationToken cancellationToken = default) =>
        _entryService.GetEntryAsync(entryId, cancellationToken);

    private static PageList ParsePage(string body)
    {
        body = body.Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new JsonException("Document pages response body was empty.");

        if (body.StartsWith('['))
        {
            var items = JsonSerializer.Deserialize<List<PageResource>>(body, JsonOptions.Default) ?? [];
            return new PageList(items, null);
        }

        var result = JsonSerializer.Deserialize<ODataList<PageResource>>(body, JsonOptions.Default)
            ?? throw new JsonException("Document pages response could not be deserialized.");

        return new PageList(result.Value, result.NextLink ?? result.PlainNextLink);
    }

    private static string? ResolveNextLink(string currentUrl, string? nextLink)
    {
        if (string.IsNullOrWhiteSpace(nextLink) ||
            !Uri.TryCreate(currentUrl, UriKind.Absolute, out var current))
            return null;

        if (!Uri.TryCreate(current, nextLink, out var resolved) ||
            (resolved.Scheme != Uri.UriSchemeHttp && resolved.Scheme != Uri.UriSchemeHttps) ||
            !string.Equals(resolved.Scheme, current.Scheme, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(resolved.Authority, current.Authority, StringComparison.OrdinalIgnoreCase))
        {
            throw new JsonException($"Document pages nextLink points outside the active Laserfiche API host: {nextLink}");
        }

        return resolved.AbsoluteUri;
    }

    private static string? GetFileName(ContentDispositionHeaderValue? disposition)
    {
        var value = disposition?.FileNameStar ?? disposition?.FileName;
        return string.IsNullOrWhiteSpace(value)
            ? null
            : value.Trim().Trim('"');
    }

    private static string? GetExtension(string? fileName, string contentType)
    {
        if (!string.IsNullOrWhiteSpace(fileName))
        {
            var extension = Path.GetExtension(fileName);
            if (!string.IsNullOrWhiteSpace(extension)) return extension;
        }

        return contentType.ToLowerInvariant() switch
        {
            "application/pdf" => ".pdf",
            "image/png" => ".png",
            "image/jpeg" => ".jpg",
            "image/webp" => ".webp",
            _ => null
        };
    }

    private sealed record PageList(List<PageResource> Items, string? NextLink);

    private sealed record ODataList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }

        [JsonPropertyName("nextLink")]
        public string? PlainNextLink { get; init; }
    }

    private sealed record PageResource
    {
        [JsonPropertyName("pageNumber")]
        public int PageNumber { get; init; }

        [JsonPropertyName("width")]
        public int? Width { get; init; }

        [JsonPropertyName("height")]
        public int? Height { get; init; }

        [JsonPropertyName("mimeType")]
        public string? MimeType { get; init; }
    }
}
