using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Retrieves template definitions from the Laserfiche Repository API v1
/// <c>GET /TemplateDefinitions</c> endpoint. Returns an empty list gracefully
/// when the endpoint is unavailable or the repository has no templates configured.
/// </summary>
internal sealed class LaserficheTemplateService : ILaserficheTemplateService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRepositoryContext  _repositoryContext;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly ILogger<LaserficheTemplateService> _logger;

    public LaserficheTemplateService(
        IHttpClientFactory httpClientFactory,
        IRepositoryContext  repositoryContext,
        ILaserficheApiAdapter adapter,
        ILogger<LaserficheTemplateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repositoryContext  = repositoryContext;
        _adapter            = adapter;
        _logger             = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<LFTemplateDefinition>> GetTemplateDefinitionsAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var repo = await _repositoryContext
                .GetActiveRepositoryAsync(cancellationToken)
                .ConfigureAwait(false);

            var url = _adapter.BuildTemplateDefinitionsUrl(repo.RepositoryId);
            _logger.LogDebug("Fetching template definitions: {Url}", url);

            using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
            using var response = await client.GetAsync(url, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Template definitions endpoint returned {Status}. " +
                    "Repository may not have templates configured.", response.StatusCode);
                return [];
            }

            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var result = JsonSerializer.Deserialize<ODataList<TemplateDefinitionResource>>(
                body, JsonOptions.Default);

            if (result?.Value is null || result.Value.Count == 0)
            {
                _logger.LogInformation("No template definitions returned from {Url}.", url);
                return [];
            }

            var templates = result.Value
                .Where(t => !string.IsNullOrWhiteSpace(t.Name))
                .Select(t => new LFTemplateDefinition
                {
                    Id          = t.Id,
                    Name        = t.Name.Trim(),
                    Description = t.Description
                })
                .ToList()
                .AsReadOnly();

            _logger.LogInformation(
                "Loaded {Count} template definitions from {RepositoryId}.",
                templates.Count, repo.RepositoryId);

            return templates;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Could not retrieve template definitions from Laserfiche. " +
                "Template stats will be empty.");
            return [];
        }
    }

    // ── Private models ─────────────────────────────────────────────────────

    private sealed record ODataList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];
    }

    private sealed record TemplateDefinitionResource
    {
        [JsonPropertyName("id")]
        public int Id { get; init; }

        [JsonPropertyName("name")]
        public string Name { get; init; } = string.Empty;

        [JsonPropertyName("description")]
        public string? Description { get; init; }
    }

    // ── Shared JSON options ───────────────────────────────────────────────

    private static class JsonOptions
    {
        public static readonly JsonSerializerOptions Default = new()
        {
            PropertyNameCaseInsensitive = true
        };
    }
}
