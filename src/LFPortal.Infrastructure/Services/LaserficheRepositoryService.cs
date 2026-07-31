using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Implements repository-level operations by calling the Laserfiche Repository API v2
/// <c>/Repositories</c> endpoint. All data returned by this service is sourced directly
/// from the live API with no local caching.
/// </summary>
internal sealed class LaserficheRepositoryService : ILaserficheRepositoryService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IRepositoryContext _repositoryContext;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly ILogger<LaserficheRepositoryService> _logger;

    /// <summary>Initialises the service with all required dependencies.</summary>
    public LaserficheRepositoryService(
        IHttpClientFactory httpClientFactory,
        IRepositoryContext repositoryContext,
        ILaserficheApiAdapter adapter,
        ILogger<LaserficheRepositoryService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _repositoryContext = repositoryContext;
        _adapter = adapter;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<RepositoryInfo>> DiscoverRepositoriesAsync(
        string serverUrl,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Discovering repositories on server {ServerUrl}.", serverUrl);

        var url = _adapter.BuildRepositoriesUrl();

        using var client = _httpClientFactory.CreateClient("LaserficheRaw");

        using var response = await client
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Repository discovery returned HTTP {StatusCode}. " +
                "Ensure EnableGetRepositoryListApi is true in the API Server appsettings.json.",
                (int)response.StatusCode);

            throw new LaserficheException(
                $"Repository discovery failed with HTTP {(int)response.StatusCode}. " +
                "The GET /Repositories endpoint must be enabled in the API Server configuration " +
                "(EnableGetRepositoryListApi: true).",
                (int)response.StatusCode);
        }

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        var result = JsonSerializer.Deserialize<ODataList<RepositoryResource>>(body, JsonOptions.Default);

        if (result?.Value is null)
        {
            return [];
        }

        return result.Value
            .Select(r => new RepositoryInfo
            {
                RepositoryId   = r.RepositoryId,
                RepositoryName = r.RepositoryName,
                ServerVersion  = r.ServerVersion,
                ApiVersion     = _adapter.ApiVersion
            })
            .ToList()
            .AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<RepositoryInfo> GetRepositoryInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildRepositoryInfoUrl(repo.RepositoryId);

        _logger.LogInformation("→ GET {Url}", url);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        using var response = await client
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        await EnsureSuccessAsync(response, url, cancellationToken).ConfigureAwait(false);

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        var resource = JsonSerializer.Deserialize<RepositoryResource>(body, JsonOptions.Default)
            ?? throw new LaserficheException(
                "Repository info response was empty or could not be deserialised.",
                (int)response.StatusCode);

        return new RepositoryInfo
        {
            RepositoryId   = resource.RepositoryId,
            RepositoryName = resource.RepositoryName,
            ServerVersion  = resource.ServerVersion,
            ApiVersion     = _adapter.ApiVersion
        };
    }

    /// <inheritdoc />
    public async Task<ConnectionStatus> TestConnectionAsync(
        CancellationToken cancellationToken = default)
    {
        try
        {
            var info = await GetRepositoryInfoAsync(cancellationToken).ConfigureAwait(false);
            return ConnectionStatus.Success(info);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test failed.");
            return ConnectionStatus.Failure(ex.Message);
        }
    }

    /// <inheritdoc />
    public async Task<ConnectionStatus> TestConnectionWithCredentialsAsync(
        string serverUrl,
        string repositoryId,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Use the serverUrl supplied by the caller — NOT the stored config —
            // so the test hits exactly what the user typed into the Settings form.
            var tokenUrl = _adapter.BuildTokenUrlFor(serverUrl, repositoryId);
            var repoUrl  = _adapter.BuildRepositoryInfoUrlFor(serverUrl, repositoryId);

            _logger.LogInformation(
                "Test connection → POST {TokenUrl}", tokenUrl);

            using var client = _httpClientFactory.CreateClient("LaserficheRaw");

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"]   = username,
                ["password"]   = password
            });

            using var tokenResponse = await client
                .PostAsync(tokenUrl, form, cancellationToken)
                .ConfigureAwait(false);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                var tokenBody404 = await tokenResponse.Content
                    .ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "Token request failed: HTTP {Status} from {Url}. Body: {Body}",
                    (int)tokenResponse.StatusCode, tokenUrl, tokenBody404);

                return ConnectionStatus.Failure(
                    $"Authentication failed: HTTP {(int)tokenResponse.StatusCode}. " +
                    $"URL attempted: {tokenUrl}. Check the Server URL, API version, and credentials.");
            }

            var tokenBody = await tokenResponse.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            using var doc = JsonDocument.Parse(tokenBody);
            if (!doc.RootElement.TryGetProperty("access_token", out var tokenEl))
            {
                return ConnectionStatus.Failure("Authentication succeeded but no token was returned.");
            }

            var token = tokenEl.GetString() ?? string.Empty;

            _logger.LogInformation(
                "Test connection → GET {RepoUrl}", repoUrl);

            using var repoRequest = new HttpRequestMessage(HttpMethod.Get, repoUrl);
            repoRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var repoResponse = await client
                .SendAsync(repoRequest, cancellationToken)
                .ConfigureAwait(false);

            if (!repoResponse.IsSuccessStatusCode)
            {
                var body404 = await repoResponse.Content
                    .ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                _logger.LogWarning(
                    "Repository info failed: HTTP {Status} from {Url}. Body: {Body}",
                    (int)repoResponse.StatusCode, repoUrl, body404);

                return ConnectionStatus.Failure(
                    $"Authentication succeeded but repository '{repositoryId}' returned " +
                    $"HTTP {(int)repoResponse.StatusCode}. " +
                    $"URL attempted: {repoUrl} — check the Repository ID and API version.");
            }

            var repoBody = await repoResponse.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            var resource = JsonSerializer.Deserialize<RepositoryResource>(repoBody, JsonOptions.Default);

            return ConnectionStatus.Success(new RepositoryInfo
            {
                RepositoryId   = resource?.RepositoryId ?? repositoryId,
                RepositoryName = resource?.RepositoryName ?? repositoryId,
                ServerVersion  = resource?.ServerVersion ?? "Unknown",
                ApiVersion     = _adapter.ApiVersion
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test with explicit credentials failed.");
            return ConnectionStatus.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Throws a <see cref="LaserficheException"/> if the HTTP response indicates an error,
    /// including the response body in the exception message for diagnostics.
    /// </summary>
    private static async Task EnsureSuccessAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode) return;

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        throw new LaserficheException(
            $"Laserfiche API returned HTTP {(int)response.StatusCode} for {url}. Body: {body}",
            (int)response.StatusCode);
    }

    // ──────────────────────────── Response models ────────────────────────────

    private sealed record ODataList<T>
    {
        [JsonPropertyName("value")]
        public List<T> Value { get; init; } = [];
    }

    private sealed record RepositoryResource
    {
        [JsonPropertyName("repositoryId")]
        public string RepositoryId { get; init; } = string.Empty;

        [JsonPropertyName("repositoryName")]
        public string RepositoryName { get; init; } = string.Empty;

        [JsonPropertyName("serverVersion")]
        public string ServerVersion { get; init; } = string.Empty;
    }
}
