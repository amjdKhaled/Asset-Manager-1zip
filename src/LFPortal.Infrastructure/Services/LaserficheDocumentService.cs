using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Implements document retrieval operations by calling the Laserfiche Repository API v2
/// document-related endpoints. Electronic documents and page images are streamed directly
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
    public async Task StreamEdocAsync(
        int entryId,
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.Edoc);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

        using var response = await client
            .GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new LaserficheException(
                $"Electronic document not available for entry {entryId}: " +
                $"HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode);
        }

        await using var contentStream = await response.Content
            .ReadAsStreamAsync(cancellationToken)
            .ConfigureAwait(false);

        await contentStream.CopyToAsync(destination, cancellationToken).ConfigureAwait(false);
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
