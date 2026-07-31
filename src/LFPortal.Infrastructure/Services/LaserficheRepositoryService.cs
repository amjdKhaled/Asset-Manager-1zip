using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.Adapters;
using Microsoft.Extensions.Logging;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Implements repository-level operations by calling the Laserfiche Repository API v1
/// <c>GET /Repositories</c> endpoint. All data returned by this service is sourced directly
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
        string repositoryId,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Discovering repositories on server {ServerUrl}.", serverUrl);

        using var client = _httpClientFactory.CreateClient("LaserficheRaw");
        var token = await RequestTokenWithCredentialsAsync(
            client,
            _adapter.BuildTokenUrlFor(serverUrl, repositoryId),
            username,
            password,
            cancellationToken).ConfigureAwait(false);

        var url = _adapter.BuildRepositoriesUrlFor(serverUrl);
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request, cancellationToken)
            .ConfigureAwait(false);
        var repositories = await ReadRepositoriesAsync(response, url, cancellationToken)
            .ConfigureAwait(false);

        return repositories.Select(ToRepositoryInfo).ToList().AsReadOnly();
    }

    /// <inheritdoc />
    public async Task<RepositoryInfo> GetRepositoryInfoAsync(
        CancellationToken cancellationToken = default)
    {
        var repo = await _repositoryContext
            .GetActiveRepositoryAsync(cancellationToken)
            .ConfigureAwait(false);

        var url = _adapter.BuildRepositoriesUrl();

        _logger.LogInformation(
            "Checking configured repository {RepositoryId} using documented GET /Repositories: {Url}.",
            repo.RepositoryId,
            url);

        using var client = _httpClientFactory.CreateClient("LaserficheAuthenticated");
        var repositories = await GetRepositoriesAsync(client, url, cancellationToken)
            .ConfigureAwait(false);

        return FindConfiguredRepository(repositories, repo.RepositoryId, url);
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
            var repositoriesUrl = _adapter.BuildRepositoriesUrlFor(serverUrl);

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
                "Authentication succeeded. Test connection → GET documented repository list {RepositoriesUrl}.",
                repositoriesUrl);

            using var repoRequest = new HttpRequestMessage(HttpMethod.Get, repositoriesUrl);
            repoRequest.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            using var repoResponse = await client
                .SendAsync(repoRequest, cancellationToken)
                .ConfigureAwait(false);

            var repositories = await ReadRepositoriesAsync(
                repoResponse, repositoriesUrl, cancellationToken).ConfigureAwait(false);
            var matched = FindConfiguredRepository(repositories, repositoryId, repositoriesUrl);

            return ConnectionStatus.Success(matched);
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
    private async Task<IReadOnlyList<RepositoryResource>> GetRepositoriesAsync(
        HttpClient client,
        string url,
        CancellationToken cancellationToken)
    {
        using var response = await client
            .GetAsync(url, cancellationToken)
            .ConfigureAwait(false);

        return await ReadRepositoriesAsync(response, url, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<string> RequestTokenWithCredentialsAsync(
        HttpClient client,
        string tokenUrl,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"] = username,
            ["password"] = password
        });

        using var response = await client
            .PostAsync(tokenUrl, form, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new LaserficheException(
                $"Authentication failed with HTTP {(int)response.StatusCode}. " +
                $"URL attempted: {tokenUrl}. Response body: {body}",
                (int)response.StatusCode);
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("access_token", out var tokenElement) ||
            string.IsNullOrWhiteSpace(tokenElement.GetString()))
        {
            throw new LaserficheException(
                $"Authentication succeeded but no access_token was returned by {tokenUrl}. " +
                $"Response body: {body}",
                (int)response.StatusCode);
        }

        return tokenElement.GetString()!;
    }

    private async Task<IReadOnlyList<RepositoryResource>> ReadRepositoriesAsync(
        HttpResponseMessage response,
        string url,
        CancellationToken cancellationToken)
    {
        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new LaserficheException(
                $"Laserfiche API returned HTTP {(int)response.StatusCode} for documented " +
                $"GET /Repositories at {url}. Body: {body}",
                (int)response.StatusCode);
        }

        var result = JsonSerializer.Deserialize<RepositoryListResponse>(body, JsonOptions.Default)
            ?? throw new LaserficheException(
                $"The documented GET /Repositories response was empty or could not be " +
                $"deserialised. URL: {url}. Body: {body}",
                (int)response.StatusCode);

        return result.Repositories.AsReadOnly();
    }

    private RepositoryInfo FindConfiguredRepository(
        IReadOnlyList<RepositoryResource> repositories,
        string repositoryId,
        string url)
    {
        var match = repositories.FirstOrDefault(r =>
            string.Equals(r.RepositoryId, repositoryId, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = string.Join(
                ", ",
                repositories
                    .Where(r => !string.IsNullOrWhiteSpace(r.RepositoryId))
                    .Select(r => r.RepositoryId));

            throw new LaserficheException(
                $"Authentication succeeded, but configured repository '{repositoryId}' " +
                $"was not returned by documented GET /Repositories at {url}. " +
                $"Repositories returned: [{available}]",
                404);
        }

        return ToRepositoryInfo(match);
    }

    private RepositoryInfo ToRepositoryInfo(RepositoryResource resource) =>
        new()
        {
            RepositoryId = resource.RepositoryId,
            RepositoryName = string.IsNullOrWhiteSpace(resource.RepositoryName)
                ? resource.RepositoryId
                : resource.RepositoryName,
            ServerVersion = resource.ServerVersion,
            ApiVersion = _adapter.ApiVersion
        };

    // ──────────────────────────── Response models ────────────────────────────

    private sealed record RepositoryListResponse
    {
        [JsonPropertyName("value")]
        public List<RepositoryResource> Value { get; init; } = [];

        [JsonPropertyName("repositories")]
        public List<RepositoryResource> RepositoriesProperty { get; init; } = [];

        [JsonIgnore]
        public List<RepositoryResource> Repositories =>
            Value.Count > 0 ? Value : RepositoriesProperty;
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
