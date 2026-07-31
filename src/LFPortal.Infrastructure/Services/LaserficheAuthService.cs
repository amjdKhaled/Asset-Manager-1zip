using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Adapters;
using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Acquires and caches Laserfiche Bearer tokens using the password-grant flow
/// against the Repository API v2 <c>/Token</c> endpoint.
/// </summary>
/// <remarks>
/// <para>
/// Tokens are cached in an <see cref="IMemoryCache"/> under a key derived from the
/// repository key. The cache entry expires 60 seconds before the token's reported
/// <c>expires_in</c> value to provide a safety margin against clock skew.
/// </para>
/// <para>
/// Token requests are not retried automatically — if the credential store returns
/// invalid credentials, the <see cref="LaserficheAuthService"/> propagates the
/// <see cref="Domain.Exceptions.LaserficheException"/> to the caller without caching
/// a failure result.
/// </para>
/// </remarks>
internal sealed class LaserficheAuthService : ILaserficheAuthService
{
    private const int EarlyExpiryBufferSeconds = 60;
    private const string CacheKeyPrefix = "LFToken:";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICredentialProvider _credentialProvider;
    private readonly ILaserficheApiAdapter _adapter;
    private readonly IMemoryCache _cache;
    private readonly LaserficheOptions _options;
    private readonly ILogger<LaserficheAuthService> _logger;

    /// <summary>Initialises the auth service with all required dependencies.</summary>
    public LaserficheAuthService(
        IHttpClientFactory httpClientFactory,
        ICredentialProvider credentialProvider,
        ILaserficheApiAdapter adapter,
        IMemoryCache cache,
        IOptions<LaserficheOptions> options,
        ILogger<LaserficheAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialProvider = credentialProvider;
        _adapter = adapter;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = $"{CacheKeyPrefix}{repository.Key}";

        if (_cache.TryGetValue(cacheKey, out string? cachedToken) && cachedToken is not null)
        {
            return cachedToken;
        }

        _logger.LogDebug(
            "Token cache miss for repository {Key}. Acquiring new token.",
            repository.Key);

        var credentials = await _credentialProvider
            .GetCredentialsAsync(repository.Key, cancellationToken)
            .ConfigureAwait(false);

        var tokenUrl = _adapter.BuildTokenUrl(repository.RepositoryId);

        _logger.LogInformation("→ POST {TokenUrl} (acquiring token)", tokenUrl);

        var tokenResponse = await RequestTokenAsync(tokenUrl, credentials.Username, credentials.Password, cancellationToken)
            .ConfigureAwait(false);

        var expirySeconds = Math.Max(tokenResponse.ExpiresIn - EarlyExpiryBufferSeconds, 30);

        _cache.Set(cacheKey, tokenResponse.AccessToken, TimeSpan.FromSeconds(expirySeconds));

        _logger.LogDebug(
            "Token acquired for repository {Key}. Expires in {Seconds}s (cached for {CacheSeconds}s).",
            repository.Key,
            tokenResponse.ExpiresIn,
            expirySeconds);

        return tokenResponse.AccessToken;
    }

    /// <inheritdoc />
    public Task InvalidateTokenAsync(RepositoryDescriptor repository)
    {
        var cacheKey = $"{CacheKeyPrefix}{repository.Key}";
        _cache.Remove(cacheKey);
        _logger.LogDebug("Token cache invalidated for repository {Key}.", repository.Key);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Posts a password-grant token request to the Laserfiche <c>/Token</c> endpoint
    /// and deserialises the response.
    /// </summary>
    private async Task<TokenResponse> RequestTokenAsync(
        string tokenUrl,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("LaserficheRaw");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["username"]   = username,
            ["password"]   = password
        });

        using var response = await client
            .PostAsync(tokenUrl, form, cancellationToken)
            .ConfigureAwait(false);

        var body = await response.Content.ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Token request failed: HTTP {StatusCode}. URL: {Url}.",
                (int)response.StatusCode,
                tokenUrl);
            throw new Domain.Exceptions.LaserficheException(
                $"Token request failed with HTTP {(int)response.StatusCode}. " +
                "Verify that the Laserfiche credentials are correct and the API Server is reachable.",
                (int)response.StatusCode);
        }

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions.Default);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            _logger.LogError("Token response from {Url} was empty or malformed.", tokenUrl);
            throw new Domain.Exceptions.LaserficheException(
                "The Laserfiche API Server returned an empty token response. " +
                "Ensure the API Server is running and the repository ID is correct.",
                (int)response.StatusCode);
        }

        return tokenResponse;
    }

    /// <summary>Deserialisation target for the Laserfiche token endpoint response.</summary>
    private sealed record TokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; init; } = string.Empty;

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; init; } = 3600;

        [JsonPropertyName("token_type")]
        public string TokenType { get; init; } = "Bearer";
    }
}
