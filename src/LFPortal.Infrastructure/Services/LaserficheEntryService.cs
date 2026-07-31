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
/// Provides entry-level operations by calling the Laserfiche Repository API v2
/// <c>/Entries</c> endpoints. All data is sourced directly from the live API.
/// </summary>
internal sealed class LaserficheEntryService : ILaserficheEntryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRepositoryContext _repositoryContext;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly ILogger<LaserficheEntryService> _logger;

    /// <summary>Initialises the service with all required dependencies.</summary>
    public LaserficheEntryService(
        IHttpClientFactory httpClientFactory,
        IRepositoryContext repositoryContext,
        ILaserficheApiAdapter adapter,
        ILogger<LaserficheEntryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repositoryContext = repositoryContext;
        _adapter = adapter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<LFEntry> GetEntryAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var url = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.Details);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, url, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var resource = JsonSerializer.Deserialize<EntryApiResource>(body, JsonOptions.Default)
            ?? throw new LaserficheException("Entry response was empty.", (int)response.StatusCode);

        return MapEntry(resource);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LFFieldValue>> GetEntryFieldsAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var url = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.Fields);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, url, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ODataList<FieldResource>>(body, JsonOptions.Default);

        return result?.Value.Select(f => new LFFieldValue
        {
            FieldName   = f.Name,
            Value       = f.Value,
            FieldType   = f.FieldType,
            IsRequired  = f.IsRequired,
            IsMultiValue = f.IsMultiValue
        }).ToList().AsReadOnly() ?? (IReadOnlyList<LFFieldValue>)[];
    }

    /// <inheritdoc />
    public async Task<LFTemplate?> GetEntryTemplateAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetEntryAsync(entryId, cancellationToken).ConfigureAwait(false);

        if (entry.TemplateId is null || string.IsNullOrWhiteSpace(entry.TemplateName))
        {
            return null;
        }

        var fields = await GetEntryFieldsAsync(entryId, cancellationToken).ConfigureAwait(false);

        return new LFTemplate
        {
            Id   = entry.TemplateId.Value,
            Name = entry.TemplateName,
            Fields = fields.Select(f => new LFFieldDefinition
            {
                Name        = f.FieldName,
                FieldType   = f.FieldType ?? "String",
                IsRequired  = f.IsRequired,
                IsMultiValue = f.IsMultiValue
            }).ToList().AsReadOnly()
        };
    }

    /// <inheritdoc />
    public async Task<string> GetEntryPathAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var entry = await GetEntryAsync(entryId, cancellationToken).ConfigureAwait(false);
        return entry.FullPath;
    }

    /// <inheritdoc />
    public async Task<PagedResult<LFEntry>> GetEntryChildrenAsync(
        int entryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken).ConfigureAwait(false);
        var skip = (page - 1) * pageSize;
        var baseUrl = _adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.Children);
        var url = $"{baseUrl}?$top={pageSize}&$skip={skip}&$count=true&orderby=name asc";

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, url, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var result = JsonSerializer.Deserialize<ODataCountList<EntryApiResource>>(body, JsonOptions.Default);

        var items = result?.Value.Select(MapEntry).ToList() ?? [];
        var totalCount = result?.Count ?? items.Count;

        return new PagedResult<LFEntry>
        {
            Items      = items.AsReadOnly(),
            TotalCount = totalCount,
            PageNumber = page,
            PageSize   = pageSize
        };
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LFEntry>> GetFolderTreeAsync(
        int rootEntryId,
        int depth,
        CancellationToken cancellationToken = default)
    {
        if (depth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Folder tree depth must be between 1 and 5.");
        }

        var result = new List<LFEntry>();
        await TraverseFolderAsync(rootEntryId, depth, 0, result, cancellationToken).ConfigureAwait(false);
        return result.AsReadOnly();
    }

    /// <summary>
    /// Recursively traverses folders up to <paramref name="maxDepth"/> levels deep,
    /// collecting folder entries into <paramref name="accumulator"/>.
    /// </summary>
    private async Task TraverseFolderAsync(
        int folderId,
        int maxDepth,
        int currentDepth,
        List<LFEntry> accumulator,
        CancellationToken cancellationToken)
    {
        if (currentDepth >= maxDepth) return;

        var page = await GetEntryChildrenAsync(folderId, 1, 200, cancellationToken)
            .ConfigureAwait(false);

        foreach (var child in page.Items)
        {
            if (child.EntryType == LFEntryType.Folder)
            {
                accumulator.Add(child);
                await TraverseFolderAsync(
                    child.Id, maxDepth, currentDepth + 1, accumulator, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }

    // ──────────────────────────── Mapping ─────────────────────────────────

    private static LFEntry MapEntry(EntryApiResource r) => new()
    {
        Id                 = r.Id,
        Name               = r.Name,
        ParentId           = r.ParentId,
        FullPath           = r.FullPath,
        FolderPath         = r.FolderPath,
        Creator            = r.Creator,
        CreationTime       = r.CreationTime,
        LastModifiedTime   = r.LastModifiedTime,
        EntryType          = ParseEntryType(r.EntryType),
        TemplateName       = r.TemplateName,
        TemplateId         = r.TemplateId,
        FileSizeBytes      = r.FileSizeBytes,
        PageCount          = r.PageCount,
        RowNumber          = r.RowNumber
    };

    private static LFEntryType ParseEntryType(string? raw) => raw?.ToLowerInvariant() switch
    {
        "document"     => LFEntryType.Document,
        "folder"       => LFEntryType.Folder,
        "shortcut"     => LFEntryType.Shortcut,
        "recordseries" => LFEntryType.RecordSeries,
        _              => LFEntryType.Unknown
    };

    // ──────────────────────────── Helpers ─────────────────────────────────

    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        throw new LaserficheException(
            $"Laserfiche API returned HTTP {(int)response.StatusCode} for {url}. Body: {body}",
            (int)response.StatusCode);
    }

    // ──────────────────────────── Response models ──────────────────────────

    private sealed record ODataList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];
    }

    private sealed record ODataCountList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];

        [JsonPropertyName("@odata.count")]
        public int Count { get; init; }
    }

    private sealed record EntryApiResource
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

        [JsonPropertyName("templateName")]
        public string? TemplateName { get; init; }

        [JsonPropertyName("templateId")]
        public int? TemplateId { get; init; }

        [JsonPropertyName("fileSizeBytes")]
        public long? FileSizeBytes { get; init; }

        [JsonPropertyName("pageCount")]
        public int? PageCount { get; init; }

        [JsonPropertyName("rowNumber")]
        public int? RowNumber { get; init; }
    }

    private sealed record FieldResource
    {
        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("value")]
        public string? Value { get; init; }

        [JsonPropertyName("fieldType")]
        public string? FieldType { get; init; }

        [JsonPropertyName("isRequired")]
        public bool IsRequired { get; init; }

        [JsonPropertyName("isMultiValue")]
        public bool IsMultiValue { get; init; }
    }
}
