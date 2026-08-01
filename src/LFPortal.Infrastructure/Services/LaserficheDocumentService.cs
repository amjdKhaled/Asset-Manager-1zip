using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Implements document retrieval operations by calling the Laserfiche Repository API v1
/// document-related endpoints. Electronic documents are streamed directly
/// from the Laserfiche server without buffering on the portal server.
/// </summary>
internal sealed class LaserficheDocumentService : ILaserficheDocumentService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRepositoryContext _repositoryContext;
    private readonly ILaserficheEntryService _entryService;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly ILogger<LaserficheDocumentService> _logger;

    /// <summary>Initialises the service with all required dependencies.</summary>
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

    /// <inheritdoc />
    public async Task<IReadOnlyList<LFDocumentPage>> GetDocumentPagesAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.Pages);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var response = await client
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetDocumentPages returned HTTP {StatusCode} for entry {EntryId}.",
                (int)response.StatusCode,
                entryId);

            return [];
        }

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<ODataList<PageResource>>(body, JsonOptions.Default);

        return result?.Value.Select(p => new LFDocumentPage
        {
            PageNumber = p.PageNumber,
            Width      = p.Width,
            Height     = p.Height,
            MimeType   = p.MimeType
        }).ToList().AsReadOnly() ?? (IReadOnlyList<LFDocumentPage>)[];
    }

    /// <inheritdoc />
    public async Task<LaserficheEdocStream> StreamEdocAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.Edoc);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("*/*"));

        var response = await client
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var statusCode = (int)response.StatusCode;
            response.Dispose();
            throw new LaserficheException(
                $"Electronic document request failed for entry {entryId}: HTTP {statusCode}.",
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

    /// <inheritdoc />
    public async Task<Stream> GetPageImageAsync(
        int entryId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
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
            response.Dispose();
            throw new LaserficheException(
                $"Page image not available for entry {entryId} page {pageNumber}: " +
                $"HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode);
        }

        // Return the response content stream; caller is responsible for disposal.
        return await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<LFEntry> GetDocumentMetadataAsync(
        int entryId,
        CancellationToken cancellationToken = default) =>
        _entryService.GetEntryAsync(entryId, cancellationToken);

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

    // ──────────────────────────── Response models ──────────────────────────

    private sealed record ODataList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];
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
