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
        // Use the Swagger-documented OData-typed folder-children path.
        // No OData query parameters — this server returns HTTP 400 for $top/$skip/$count/$select
        // on the folder-children endpoint. Fetch all children and slice in memory.
        var url = _adapter.BuildFolderChildrenUrl(repo.RepositoryId, entryId);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        await EnsureSuccessAsync(response, url, cancellationToken).ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var allEntries = ParseEntryList(body);
        var totalCount = allEntries.Count;

        // Server does not support $top/$skip — slice in memory.
        var page_items = allEntries.Skip(skip).Take(pageSize).ToList();

        return new PagedResult<LFEntry>
        {
            Items      = page_items.AsReadOnly(),
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

        // If the administrator has explicitly configured a root entry ID, use it directly.
        // This is the fast path and avoids a ByPath network call on every dashboard load.
        var configuredRootId = _adapter.GetConfiguredRootEntryId();
        if (configuredRootId > 0)
        {
            _logger.LogInformation(
                "Using configured root entry ID={Id} for repository '{RepoId}' (set in appsettings/settings).",
                configuredRootId, repo.RepositoryId);
            s_rootIdCache[repo.RepositoryId] = configuredRootId;
            return configuredRootId;
        }

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

        // Resolve the repository root via the Swagger-documented ByPath endpoint.
        // Backslash (\) is the Laserfiche root path.
        var byPathUrl = _adapter.BuildEntryByPathUrl(repo.RepositoryId, @"\");
        _logger.LogInformation("Discovering repository root via ByPath: {Url}", byPathUrl);

        using var response = await client.GetAsync(byPathUrl, cancellationToken).ConfigureAwait(false);

        // Always read and log the complete raw body BEFORE any deserialization attempt.
        var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        _logger.LogInformation(
            "===== RAW BYPATH RESPONSE (HTTP {Status}) =====\n{Body}\n===============================================",
            (int)response.StatusCode, body);

        if (!response.IsSuccessStatusCode)
        {
            throw new LaserficheException(
                $"Root entry discovery failed. GET {byPathUrl} returned HTTP {(int)response.StatusCode}. " +
                $"Verify server URL, repository ID, and credentials. Body: {body}",
                (int)response.StatusCode);
        }

        // The ByPath endpoint wraps the entry: { "entry": { "id": ..., "entryType": ..., ... } }
        var byPathResponse = JsonSerializer.Deserialize<ByPathApiResponse>(body, JsonOptions.Default);
        var entry = byPathResponse?.Entry;

        if (entry is not { Id: > 0 })
        {
            throw new LaserficheException(
                $"Root entry discovery: GET {byPathUrl} returned HTTP 200 but 'entry.id' was not found or was zero in the response body. " +
                $"Raw body: {body}",
                (int)response.StatusCode);
        }

        _logger.LogInformation(
            "Repository root discovered via ByPath: ID={Id}, name='{Name}', path='{Path}'.",
            entry.Id, entry.Name, entry.FullPath);

        s_rootIdCache[repo.RepositoryId] = entry.Id;
        return entry.Id;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LFEntry>> GetAllFolderChildrenAsync(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        // Swagger-documented endpoint only:
        // GET /Repositories/{repoId}/Entries/{id}/Laserfiche.Repository.Folder/children
        var url = _adapter.BuildFolderChildrenUrl(repo.RepositoryId, entryId);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");

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
                    "GetAllFolderChildrenAsync(entryId={EntryId}): GET {Url} → HTTP {Status}. Body: {Body}",
                    entryId, url, (int)response.StatusCode,
                    body.Length > 400 ? body[..400] + "…" : body);
                return [];
            }

            _logger.LogInformation(
                "GetAllFolderChildrenAsync(entryId={EntryId}): GET {Url} → HTTP {Status}.",
                entryId, url, (int)response.StatusCode);

            // Log full raw body so response schema can be inspected before any parse attempt.
            _logger.LogInformation(
                "===== RAW FOLDER-CHILDREN RESPONSE (entryId={EntryId}) =====\n{Body}\n==========================================================",
                entryId, body.Length > 4000 ? body[..4000] + "\n…[truncated]" : body);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetAllFolderChildrenAsync(entryId={EntryId}): GET {Url} threw.", entryId, url);
            return [];
        }

        try
        {
            var entries = ParseEntryList(body);

            if (entries.Count == 0)
            {
                // HTTP 200 but zero entries — this is never silently swallowed.
                // Log the full raw body so the response schema can be inspected.
                _logger.LogWarning(
                    "GetAllFolderChildrenAsync(entryId={EntryId}): HTTP 200 but parser produced 0 entries. " +
                    "The response schema may not match the expected OData envelope {{\"value\":[...]}}. " +
                    "Raw body: {Body}",
                    entryId, body.Length > 4000 ? body[..4000] + "…" : body);
            }
            else
            {
                _logger.LogInformation(
                    "GetAllFolderChildrenAsync(entryId={EntryId}): parsed {Count} entries. Sample types: {Types}",
                    entryId, entries.Count,
                    string.Join(", ", entries.Take(5).Select(e => e.EntryType.ToString())));
            }

            return entries.AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "GetAllFolderChildrenAsync(entryId={EntryId}): JSON parse failed. Raw body: {Body}",
                entryId, body.Length > 4000 ? body[..4000] + "…" : body);
            return [];
        }
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

    /// <summary>
    /// Wrapper returned by <c>GET /Repositories/{repo}/Entries/ByPath</c>.
    /// Schema: <c>{ "entry": { "id": N, "entryType": "...", ... } }</c>
    /// </summary>
    private sealed record ByPathApiResponse
    {
        [JsonPropertyName("entry")]
        public EntryApiResource? Entry { get; init; }
    }

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
