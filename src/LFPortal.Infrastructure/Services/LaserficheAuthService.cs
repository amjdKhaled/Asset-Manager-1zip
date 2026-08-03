using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Adapters;
using LFPortal.Infrastructure.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Services;

/// <summary>
/// Acquires and caches Laserfiche Bearer tokens using the password-grant flow
/// against the Repository API v1 token endpoint:
/// <c>{ServerUrl}{ApiBasePath}/v1/Repositories/{repositoryId}/Token</c>.
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
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ILogger<LaserficheAuthService> _logger;

    /// <summary>Initialises the auth service with all required dependencies.</summary>
    public LaserficheAuthService(
        IHttpClientFactory httpClientFactory,
        ICredentialProvider credentialProvider,
        ILaserficheApiAdapter adapter,
        IMemoryCache cache,
        IOptions<LaserficheOptions> options,
        IHttpContextAccessor httpContextAccessor,
        ILogger<LaserficheAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _credentialProvider = credentialProvider;
        _adapter = adapter;
        _httpContextAccessor = httpContextAccessor;
        _cache = cache;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>
    /// Builds the token cache key. Includes the repository id (different repositories
    /// must never share a token) and, when an HTTP session is present, the session id —
    /// so two concurrent users authenticated as different Laserfiche accounts can never
    /// reuse each other's cached token. Outside an HTTP context (background work) a
    /// process-wide scope is used, matching the disk-stored fallback credentials.
    /// </summary>
    private string CacheKeyFor(RepositoryDescriptor repository)
    {
        var scope = CurrentScope();
        return $"{CacheKeyPrefix}{repository.Key}:{repository.RepositoryId}:{scope}:g{GenerationFor(scope)}";
    }

    /// <summary>Resolves the token-cache scope for the current request.</summary>
    private string CurrentScope()
    {
        string scope = "app";
        try
        {
            var session = _httpContextAccessor.HttpContext?.Session;
            // Use the session id only for ESTABLISHED sessions (any data written).
            // An empty session has no stable id and, more importantly, uses the
            // shared disk-stored fallback credentials — sharing the "app" scope
            // is then both correct and avoids re-authenticating every request.
            if (session is not null && session.Keys.Any() && !string.IsNullOrEmpty(session.Id))
                scope = session.Id;
        }
        catch
        {
            // Session unavailable (not loaded / no session middleware) — use app scope.
        }

        return scope;
    }

    /// <summary>
    /// Per-session-scope cache-key generation counters. Incrementing a scope's
    /// generation on sign-out makes EVERY previously written token key for that
    /// scope unreachable — including keys written concurrently by in-flight
    /// requests that started before sign-out (their key embeds the old
    /// generation and is never read again; the orphaned entry expires via its
    /// normal TTL). This is race-free by construction, unlike explicit eviction.
    /// Entries are one small int per signed-out session scope, bounded by
    /// session turnover; a scope with no sign-outs stores nothing (generation 0).
    /// </summary>
    private static readonly ConcurrentDictionary<string, int> _scopeGenerations = new();

    /// <summary>Current cache-key generation for a session scope (0 unless signed out before).</summary>
    private static int GenerationFor(string scope) =>
        _scopeGenerations.TryGetValue(scope, out var gen) ? gen : 0;

    /// <inheritdoc />
    public Task InvalidateCurrentSessionTokensAsync()
    {
        var scope = CurrentScope();
        if (scope == "app")
            return Task.CompletedTask; // no established session — nothing user-specific is cached

        var newGen = _scopeGenerations.AddOrUpdate(scope, 1, static (_, g) => g + 1);

        _logger.LogInformation(
            "[LF AUTH] Sign out: cached tokens for the current session invalidated (generation {Generation}).",
            newGen);

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<string> GetTokenAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyFor(repository);

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

        _logger.LogInformation(
            "[LF AUTH] POST {TokenUrl} (acquiring token, repository {RepoId})",
            tokenUrl,
            repository.RepositoryId);

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
        var cacheKey = CacheKeyFor(repository);
        _cache.Remove(cacheKey);
        _logger.LogDebug("Token cache invalidated for repository {Key}.", repository.Key);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<bool> TryAuthenticateAsync(
        RepositoryDescriptor repository,
        string username,
        string password,
        CancellationToken cancellationToken = default)
    {
        var tokenUrl = _adapter.BuildTokenUrl(repository.RepositoryId);

        // Log the attempt WITHOUT logging the password.
        _logger.LogInformation(
            "[LF AUTH] POST {TokenUrl} (login attempt for repository {RepoId}, user {Username})",
            tokenUrl,
            repository.RepositoryId,
            username);

        try
        {
            var tokenResponse = await RequestTokenAsync(tokenUrl, username, password, cancellationToken)
                .ConfigureAwait(false);

            // Warm the token cache so subsequent GetTokenAsync calls skip re-authentication.
            var cacheKey      = CacheKeyFor(repository);
            var expirySeconds = Math.Max(tokenResponse.ExpiresIn - EarlyExpiryBufferSeconds, 30);
            _cache.Set(cacheKey, tokenResponse.AccessToken, TimeSpan.FromSeconds(expirySeconds));

            _logger.LogInformation(
                "[LF AUTH] Login succeeded for repository {RepoId}. Token cached for {CacheSeconds}s.",
                repository.RepositoryId,
                expirySeconds);

            return true;
        }
        catch (Domain.Exceptions.LaserficheException ex)
            when (ex.StatusCode is 400 or 401 or 403)
        {
            // Credential error — return false; do not propagate.
            _logger.LogInformation(
                "[LF AUTH] Login failed for repository {RepoId}: HTTP {StatusCode}.",
                repository.RepositoryId,
                ex.StatusCode);
            return false;
        }
        // 404 (unknown repository), other 4xx, network failures and 5xx errors
        // propagate to the caller so it can show a precise error message
        // instead of a misleading "check username and password".
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
                "[LF AUTH] Token request failed: HTTP {StatusCode}. URL: {Url}.",
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
            _logger.LogError("[LF AUTH] Token response from {Url} was empty or malformed.", tokenUrl);
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
