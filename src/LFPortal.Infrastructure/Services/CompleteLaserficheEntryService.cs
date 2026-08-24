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
/// Decorates the existing entry service and replaces folder-list operations with a
/// pagination-complete implementation. This keeps every ILaserficheEntryService consumer
/// (Dashboard, Archive, folder tree) from silently seeing only the first server page or
/// an arbitrary fixed number of pages.
/// </summary>
internal sealed class CompleteLaserficheEntryService : ILaserficheEntryService
{
    private readonly LaserficheEntryService _inner;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRepositoryContext _repositoryContext;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly ILogger<CompleteLaserficheEntryService> _logger;

    public CompleteLaserficheEntryService(
        LaserficheEntryService inner,
        IHttpClientFactory httpClientFactory,
        IRepositoryContext repositoryContext,
        ILaserficheApiAdapter adapter,
        ILogger<CompleteLaserficheEntryService> logger)
    {
        _inner = inner;
        _httpClientFactory = httpClientFactory;
        _repositoryContext = repositoryContext;
        _adapter = adapter;
        _logger = logger;
    }

    public Task<LFEntry> GetEntryAsync(int entryId, CancellationToken cancellationToken = default) =>
        _inner.GetEntryAsync(entryId, cancellationToken);

    public Task<IReadOnlyList<LFFieldValue>> GetEntryFieldsAsync(
        int entryId,
        CancellationToken cancellationToken = default) =>
        _inner.GetEntryFieldsAsync(entryId, cancellationToken);

    public Task<LFTemplate?> GetEntryTemplateAsync(
        int entryId,
        CancellationToken cancellationToken = default) =>
        _inner.GetEntryTemplateAsync(entryId, cancellationToken);

    public Task<string> GetEntryPathAsync(
        int entryId,
        CancellationToken cancellationToken = default) =>
        _inner.GetEntryPathAsync(entryId, cancellationToken);

    public Task<int> GetRootEntryIdAsync(CancellationToken cancellationToken = default) =>
        _inner.GetRootEntryIdAsync(cancellationToken);

    public async Task<PagedResult<LFEntry>> GetEntryChildrenAsync(
        int entryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
            throw new ArgumentOutOfRangeException(nameof(page), "Page must be at least 1.");
        if (pageSize < 1)
            throw new ArgumentOutOfRangeException(nameof(pageSize), "Page size must be at least 1.");

        var allEntries = await GetAllFolderChildrenAsync(entryId, cancellationToken)
            .ConfigureAwait(false);
        var skip = (page - 1) * pageSize;

        return new PagedResult<LFEntry>
        {
            Items = allEntries.Skip(skip).Take(pageSize).ToList().AsReadOnly(),
            TotalCount = allEntries.Count,
            PageNumber = page,
            PageSize = pageSize
        };
    }

    public async Task<IReadOnlyList<LFEntry>> GetAllFolderChildrenAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var firstUrl = _adapter.BuildFolderChildrenUrl(repo.RepositoryId, entryId);
        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

        var allEntries = new List<LFEntry>();
        var visitedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? nextUrl = firstUrl;
        var pageNumber = 0;

        while (!string.IsNullOrWhiteSpace(nextUrl))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!visitedUrls.Add(nextUrl))
            {
                throw new LaserficheException(
                    $"Folder children pagination repeated a nextLink for entry {entryId}: {nextUrl}",
                    500);
            }

            pageNumber++;
            using var response = await client.GetAsync(nextUrl, cancellationToken).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                throw new LaserficheException(
                    $"Laserfiche API returned HTTP {(int)response.StatusCode} while listing " +
                    $"folder {entryId}, page {pageNumber}, URL {nextUrl}. Body: {body}",
                    (int)response.StatusCode);
            }

            var parsed = ParsePage(body);
            allEntries.AddRange(parsed.Entries);
            nextUrl = ResolveNextLink(nextUrl, parsed.NextLink);

            _logger.LogInformation(
                "Complete folder listing. EntryId={EntryId}; Page={Page}; PageEntries={PageEntries}; " +
                "RunningTotal={RunningTotal}; HasNext={HasNext}.",
                entryId, pageNumber, parsed.Entries.Count, allEntries.Count, nextUrl is not null);
        }

        // Entry ID is authoritative. De-duplicate defensively in case a server repeats
        // an item at a page boundary.
        var unique = allEntries
            .GroupBy(e => e.Id)
            .Select(g => g
                .OrderByDescending(e => e.LastModifiedTime ?? e.CreationTime ?? DateTimeOffset.MinValue)
                .First())
            .ToList()
            .AsReadOnly();

        _logger.LogInformation(
            "Complete folder listing finished. EntryId={EntryId}; Pages={Pages}; UniqueEntries={Count}.",
            entryId, pageNumber, unique.Count);

        return unique;
    }

    public async Task<IReadOnlyList<LFEntry>> GetFolderTreeAsync(
        int rootEntryId,
        int depth,
        CancellationToken cancellationToken = default)
    {
        if (depth is < 1 or > 5)
            throw new ArgumentOutOfRangeException(nameof(depth), "Folder tree depth must be between 1 and 5.");

        var result = new List<LFEntry>();
        var visited = new HashSet<int>();
        await TraverseFolderAsync(rootEntryId, depth, 0, result, visited, cancellationToken)
            .ConfigureAwait(false);
        return result.AsReadOnly();
    }

    private async Task TraverseFolderAsync(
        int folderId,
        int maxDepth,
        int currentDepth,
        List<LFEntry> accumulator,
        HashSet<int> visited,
        CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth || !visited.Add(folderId))
            return;

        var children = await GetAllFolderChildrenAsync(folderId, cancellationToken)
            .ConfigureAwait(false);

        foreach (var child in children.Where(e => e.EntryType == LFEntryType.Folder))
        {
            accumulator.Add(child);
            await TraverseFolderAsync(
                    child.Id,
                    maxDepth,
                    currentDepth + 1,
                    accumulator,
                    visited,
                    cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private static EntryPage ParsePage(string body)
    {
        body = body.Trim();
        if (string.IsNullOrWhiteSpace(body))
            throw new JsonException("Folder children response body was empty.");

        if (body.StartsWith('['))
        {
            var resources = JsonSerializer.Deserialize<List<EntryResource>>(body, JsonOptions.Default) ?? [];
            return new EntryPage(resources.Select(MapEntry).ToList(), null);
        }

        var envelope = JsonSerializer.Deserialize<ODataPagedList<EntryResource>>(body, JsonOptions.Default)
            ?? throw new JsonException("Folder children response could not be deserialized.");

        return new EntryPage(
            envelope.Value.Select(MapEntry).ToList(),
            envelope.NextLink ?? envelope.PlainNextLink);
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
            throw new JsonException($"Folder children nextLink points outside the active Laserfiche API host: {nextLink}");
        }

        return resolved.AbsoluteUri;
    }

    private static LFEntry MapEntry(EntryResource r) => new()
    {
        Id = r.Id,
        Name = r.Name,
        ParentId = r.ParentId,
        FullPath = r.FullPath,
        FolderPath = r.FolderPath,
        Creator = r.Creator,
        CreationTime = r.CreationTime,
        LastModifiedTime = r.LastModifiedTime,
        EntryType = ParseEntryType(r.EntryType ?? r.ODataType),
        TemplateName = r.TemplateName,
        TemplateId = r.TemplateId,
        // Different Repository API generations use different names for the same value.
        FileSizeBytes = r.FileSizeBytes ?? r.ElectronicDocumentSize ?? r.ElecDocumentSize,
        PageCount = r.PageCount,
        RowNumber = r.RowNumber
    };

    private static LFEntryType ParseEntryType(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return LFEntryType.Unknown;

        var token = raw.TrimStart('#');
        var dot = token.LastIndexOf('.');
        if (dot >= 0)
            token = token[(dot + 1)..];

        return token.ToLowerInvariant() switch
        {
            "document" => LFEntryType.Document,
            "folder" => LFEntryType.Folder,
            "shortcut" => LFEntryType.Shortcut,
            "recordseries" => LFEntryType.RecordSeries,
            _ => LFEntryType.Unknown
        };
    }

    private sealed record EntryPage(List<LFEntry> Entries, string? NextLink);

    private sealed record ODataPagedList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];

        [JsonPropertyName("@odata.nextLink")]
        public string? NextLink { get; init; }

        [JsonPropertyName("nextLink")]
        public string? PlainNextLink { get; init; }
    }

    private sealed record EntryResource
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("parentId")]
        public int ParentId { get; init; }

        [JsonPropertyName("fullPath")]
        public string FullPath { get; init; } = string.Empty;

        [JsonPropertyName("folderPath")]
        public string FolderPath { get; init; } = string.Empty;

        [JsonPropertyName("creator")]
        public string? Creator { get; init; }

        [JsonPropertyName("creationTime")]
        public DateTimeOffset? CreationTime { get; init; }

        [JsonPropertyName("lastModifiedTime")]
        public DateTimeOffset? LastModifiedTime { get; init; }

        [JsonPropertyName("entryType")]
        public string? EntryType { get; init; }

        [JsonPropertyName("@odata.type")]
        public string? ODataType { get; init; }

        [JsonPropertyName("templateName")]
        public string? TemplateName { get; init; }

        [JsonPropertyName("templateId")]
        public int? TemplateId { get; init; }

        [JsonPropertyName("fileSizeBytes")]
        public long? FileSizeBytes { get; init; }

        [JsonPropertyName("electronicDocumentSize")]
        public long? ElectronicDocumentSize { get; init; }

        [JsonPropertyName("elecDocumentSize")]
        public long? ElecDocumentSize { get; init; }

        [JsonPropertyName("pageCount")]
        public int? PageCount { get; init; }

        [JsonPropertyName("rowNumber")]
        public int? RowNumber { get; init; }
    }

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
