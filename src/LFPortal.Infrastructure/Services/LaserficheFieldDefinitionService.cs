using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Retrieves repository-wide field definitions from
/// <c>GET /v1/Repositories/{repoId}/FieldDefinitions</c>.
/// Confirmed available on this installation.
/// </summary>
internal sealed class LaserficheFieldDefinitionService : ILaserficheFieldDefinitionService
{
    private readonly IHttpClientFactory     _httpClientFactory;
    private readonly IRepositoryContext     _repositoryContext;
    private readonly ILaserficheApiAdapter  _adapter;
    private readonly ILogger<LaserficheFieldDefinitionService> _logger;

    public LaserficheFieldDefinitionService(
        IHttpClientFactory  httpClientFactory,
        IRepositoryContext  repositoryContext,
        ILaserficheApiAdapter adapter,
        ILogger<LaserficheFieldDefinitionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repositoryContext = repositoryContext;
        _adapter           = adapter;
        _logger            = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<int, LFFieldDefinition>> GetFieldDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildFieldDefinitionsUrl(repo.RepositoryId);

        using var client   = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "GetFieldDefinitionsAsync: GET {Url} → HTTP {Status}. Body: {Body}",
                url, (int)response.StatusCode,
                body.Length > 500 ? body[..500] + "…" : body);
            throw new LaserficheException(
                $"FieldDefinitions endpoint returned HTTP {(int)response.StatusCode}. " +
                $"Endpoint: GET {url}. Body: {body}",
                (int)response.StatusCode);
        }

        _logger.LogInformation(
            "GetFieldDefinitionsAsync: GET {Url} → HTTP {Status}.",
            url, (int)response.StatusCode);

        _logger.LogInformation(
            "===== RAW FIELD-DEFINITIONS RESPONSE =====\n{Body}\n==========================================",
            body.Length > 4000 ? body[..4000] + "\n…[truncated]" : body);

        var odata = JsonSerializer.Deserialize<ODataList<FieldDefinitionResource>>(body, JsonOptions.Default);
        var items = odata?.Value ?? [];

        _logger.LogInformation(
            "GetFieldDefinitionsAsync: parsed {Count} field definition(s).",
            items.Count);

        return items
            .Where(r => r.Id > 0)
            .ToDictionary(r => r.Id, r => new LFFieldDefinition
            {
                Id           = r.Id,
                Name         = r.Name,
                FieldType    = r.FieldType ?? string.Empty,
                IsRequired   = r.IsRequired,
                IsMultiValue = r.IsMultiValue,
                Description  = r.Description,
                MaxLength    = r.MaxLength
            })
            .AsReadOnly();
    }

    // ── Private response models ────────────────────────────────────────────

    private sealed record ODataList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];
    }

    private sealed record FieldDefinitionResource
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("fieldType")]
        public string? FieldType { get; init; }

        [JsonPropertyName("isRequired")]
        public bool IsRequired { get; init; }

        [JsonPropertyName("isMultiValue")]
        public bool IsMultiValue { get; init; }

        [JsonPropertyName("description")]
        public string? Description { get; init; }

        [JsonPropertyName("maxLength")]
        public int? MaxLength { get; init; }
    }
}
