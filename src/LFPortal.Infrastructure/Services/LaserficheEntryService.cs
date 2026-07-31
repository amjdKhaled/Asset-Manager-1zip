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
/// Provides entry-level operations by calling the Laserfiche Repository API v1
/// <c>/Entries</c> endpoints. All data is sourced directly from the live API.
/// </summary>
internal sealed class LaserficheEntryService : ILaserficheEntryService
{
    // Process-lifetime cache: repositoryId → root entry ID.
    // The root never changes while the server is running, so a static cache is safe.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int>
        s_rootIdCache = new(StringComparer.OrdinalIgnoreCase);

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
    public async Task<int> GetRootEntryIdAsync(CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        if (s_rootIdCache.TryGetValue(repo.RepositoryId, out int cached))
        {
            _logger.LogDebug("Root entry ID for {RepoId} served from cache: {Id}.", repo.RepositoryId, cached);
            return cached;
        }

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

        // ── Method 1: path-based lookup  GET /Entries?entryPath=\
        // The backslash is the LF root path. URL-encoded: %5C.
        var pathUrl = $"{_adapter.BuildEntriesUrl(repo.RepositoryId)}?entryPath=%5C&fallbackToClosestAncestor=false";
        try
        {
            using var r1 = await client.GetAsync(pathUrl, cancellationToken).ConfigureAwait(false);
            if (r1.IsSuccessStatusCode)
            {
                var body1 = await r1.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                // The path-lookup endpoint returns a single Entry object (not an OData list).
                var entry1 = JsonSerializer.Deserialize<EntryApiResource>(body1, JsonOptions.Default);
                if (entry1 is { Id: > 0 })
                {
                    _logger.LogInformation(
                        "Root entry discovered via path lookup (entryPath=\\): ID={Id}, name='{Name}'.",
                        entry1.Id, entry1.Name);
                    s_rootIdCache[repo.RepositoryId] = entry1.Id;
                    return entry1.Id;
                }
            }
            else
            {
                _logger.LogDebug("Path-based root lookup returned HTTP {Status}. Trying next method.", (int)r1.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Path-based root lookup threw. Trying next method.");
        }

        // ── Method 2: check entry 1 — if parentId == 0 then it IS the root
        var entry1Url = _adapter.BuildEntryUrl(repo.RepositoryId, 1, Adapters.EntryResource.Details);
        try
        {
            using var r2 = await client.GetAsync(entry1Url, cancellationToken).ConfigureAwait(false);
            if (r2.IsSuccessStatusCode)
            {
                var body2 = await r2.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                var entry = JsonSerializer.Deserialize<EntryApiResource>(body2, JsonOptions.Default);
                if (entry is { ParentId: 0, Id: > 0 })
                {
                    _logger.LogInformation("Root entry is ID=1 (parentId=0 confirmed).");
                    s_rootIdCache[repo.RepositoryId] = 1;
                    return 1;
                }

                _logger.LogDebug(
                    "Entry 1 exists but parentId={ParentId} — it is NOT the root. Falling back.",
                    entry?.ParentId);
            }
            else
            {
                _logger.LogDebug("GET /Entries/1 returned HTTP {Status}.", (int)r2.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Entry-1 check threw. Falling back to ID=1.");
        }

        // ── Fallback: warn and use 1
        _logger.LogWarning(
            "Could not discover repository root for '{RepoId}' via path lookup or entry-1 check. " +
            "Defaulting to entry ID=1. If the dashboard shows zero data, verify the root entry ID " +
            "in the Laserfiche Repository and set LF_ROOT_ENTRY_ID if needed.",
            repo.RepositoryId);
        s_rootIdCache[repo.RepositoryId] = 1;
        return 1;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LFEntry>> GetAllFolderChildrenAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        // URL candidates — tried in order; first 2xx response wins.
        // Primary  : OData-typed path confirmed in Swagger:
        //            /Entries/{id}/Laserfiche.Repository.Folder/children
        // Fallback1: /Folder/Children (original GovSearch AI Node.js backend path)
        // Fallback2: /children (generic lowercase, older v1 variants)
        string primaryUrl   = _adapter.BuildFolderChildrenUrl(repo.RepositoryId, entryId);
        string fallback1Url = $"{_adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.FolderChildren)}?$top=1000";
        string fallback2Url = $"{_adapter.BuildEntryUrl(repo.RepositoryId, entryId, Adapters.EntryResource.Children)}?$top=1000";

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

        foreach (var url in (string[])[primaryUrl, fallback1Url, fallback2Url])
        {
            string body;
            try
            {
                using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

                body = await response.Content
                    .ReadAsStringAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "GET {Url} → HTTP {Status}. Body: {Body}",
                        url, (int)response.StatusCode,
                        body.Length > 400 ? body[..400] + "…" : body);
                    continue;
                }

                _logger.LogInformation("GET {Url} → HTTP {Status}. Body preview: {Body}",
                    url, (int)response.StatusCode,
                    body.Length > 400 ? body[..400] + "…" : body);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GET {Url} threw an exception. Trying next URL.", url);
                continue;
            }

            try
            {
                var entries = ParseEntryList(body);

                _logger.LogInformation(
                    "GetAllFolderChildrenAsync(entryId={EntryId}): parsed {Count} entries from {Url}. " +
                    "Sample entryTypes: {Types}",
                    entryId, entries.Count, url,
                    string.Join(", ", entries.Take(5).Select(e => e.EntryType.ToString())));

                return entries.AsReadOnly();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse JSON from {Url}. Body: {Body}",
                    url, body.Length > 400 ? body[..400] + "…" : body);
                // Don't try next URL — this one returned 2xx but bad JSON; log and return empty.
                return [];
            }
        }

        _logger.LogError(
            "GetAllFolderChildrenAsync(entryId={EntryId}): all {Count} URL candidates failed. " +
            "No folder children could be retrieved.",
            entryId, 3);
        return [];
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
        // Prefer the explicit "entryType" field; fall back to the OData "@odata.type"
        // discriminator (e.g. "#Laserfiche.Repository.Folder") when entryType is absent.
        EntryType          = ParseEntryType(r.EntryType ?? r.ODataType),
        TemplateName       = r.TemplateName,
        TemplateId         = r.TemplateId,
        FileSizeBytes      = r.FileSizeBytes,
        PageCount          = r.PageCount,
        RowNumber          = r.RowNumber
    };

    /// <summary>
    /// Parses a Laserfiche API response into a list of <see cref="EntryApiResource"/> items.
    /// Handles both formats used by different v1 endpoints:
    ///   • OData envelope: <c>{"value":[...]}</c>  — returned by /children, /Searches, etc.
    ///   • Bare array:     <c>[...]</c>             — returned by /Repositories on some builds.
    /// Falls back gracefully and logs if neither format matches.
    /// </summary>
    private List<LFEntry> ParseEntryList(string body)
    {
        body = body.Trim();

        if (body.StartsWith('['))
        {
            // Bare JSON array
            var resources = JsonSerializer.Deserialize<List<EntryApiResource>>(body, JsonOptions.Default) ?? [];
            return resources.Select(MapEntry).ToList();
        }

        // OData envelope {"value":[...]}
        var odata = JsonSerializer.Deserialize<ODataList<EntryApiResource>>(body, JsonOptions.Default);
        return (odata?.Value ?? []).Select(MapEntry).ToList();
    }

    /// <summary>
    /// Maps the raw API string to <see cref="LFEntryType"/>.
    /// Handles the two forms returned by different Laserfiche API builds:
    ///   • Simple names: "Document", "Folder", "Shortcut", "RecordSeries"
    ///   • OData qualified names: "#Laserfiche.Repository.Document", etc.
    /// </summary>
    private static LFEntryType ParseEntryType(string? raw)
    {
        if (raw is null) return LFEntryType.Unknown;

        // Strip leading '#' and extract the last segment after '.'
        // e.g. "#Laserfiche.Repository.Document" → "document"
        var token = raw.TrimStart('#');
        var dot   = token.LastIndexOf('.');
        if (dot >= 0) token = token[(dot + 1)..];

        return token.ToLowerInvariant() switch
        {
            "document"     => LFEntryType.Document,
            "folder"       => LFEntryType.Folder,
            "shortcut"     => LFEntryType.Shortcut,
            "recordseries" => LFEntryType.RecordSeries,
            _              => LFEntryType.Unknown
        };
    }

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

        /// <summary>OData type discriminator, e.g. "#Laserfiche.Repository.Folder".</summary>
        [JsonPropertyName("@odata.type")]
        public string? ODataType { get; init; }

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
