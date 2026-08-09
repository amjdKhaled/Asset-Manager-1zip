using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
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
/// against the Repository API token endpoint.
/// </summary>
/// <remarks>
/// <para>
/// <strong>Single-flight acquisition</strong>: a per-cache-key
/// <see cref="SemaphoreSlim"/> guarantees that when the token cache is empty
/// and N requests arrive simultaneously for the same key, exactly ONE token
/// POST is sent to the Laserfiche API.  All other concurrent callers wait for
/// the in-flight request and share its result.  This prevents the "token storm"
/// that causes HTTP 429 when many parallel dashboard API calls all experience a
/// cache miss at the same time.
/// </para>
/// <para>
/// <strong>Double-checked locking</strong>: the cache is checked once before
/// acquiring the semaphore (fast path, no lock) and again immediately after
/// (slow path, under lock), so a token written by a concurrent winner is reused
/// without a redundant POST.
/// </para>
/// <para>
/// <strong>Bounded 429 retry</strong>: when the token endpoint responds with
/// HTTP 429, the implementation retries up to <see cref="MaxTokenRetries"/>
/// times, honouring the <c>Retry-After</c> header when present and falling
/// back to conservative exponential back-off (1 s, 2 s) otherwise.
/// Deterministic 4xx errors (400, 401, 403, 404) are never retried.
/// </para>
/// <para>
/// Tokens are cached in an <see cref="IMemoryCache"/> under a key derived from
/// the repository key.  The cache entry expires 60 seconds before the token's
/// reported <c>expires_in</c> value to provide a safety margin.
/// </para>
/// </remarks>
internal sealed class LaserficheAuthService : ILaserficheAuthService
{
    private const int EarlyExpiryBufferSeconds = 60;
    private const int MaxTokenRetries          = 2;
    private const string CacheKeyPrefix        = "LFToken:";

    private readonly IHttpClientFactory      _httpClientFactory;
    private readonly ICredentialProvider     _credentialProvider;
    private readonly ILaserficheApiAdapter   _adapter;
    private readonly IMemoryCache            _cache;
    private readonly LaserficheOptions       _options;
    private readonly IHttpContextAccessor    _httpContextAccessor;
    private readonly ILogger<LaserficheAuthService> _logger;

    // Per-cache-key semaphores that guarantee single-flight token acquisition.
    // A new SemaphoreSlim(1,1) is created on first use for each key and kept for
    // the lifetime of the service.  Memory impact is negligible (≈100 B each)
    // because the number of unique keys is bounded by active sessions × repos.
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _keyLocks = new();

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
        _httpClientFactory   = httpClientFactory;
        _credentialProvider  = credentialProvider;
        _adapter             = adapter;
        _httpContextAccessor = httpContextAccessor;
        _cache               = cache;
        _options             = options.Value;
        _logger              = logger;
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
    /// <remarks>
    /// <para>
    /// <strong>Algorithm</strong>:
    /// <list type="number">
    ///   <item>Check cache — if a valid token exists, return it immediately (no lock).</item>
    ///   <item>Acquire the per-key <see cref="SemaphoreSlim"/> (async, respects cancellation).</item>
    ///   <item>Check cache again — another concurrent caller may have populated it while we waited.</item>
    ///   <item>If still empty, call <see cref="RequestTokenAsync"/> (this is the sole HTTP POST).</item>
    ///   <item>Store the token in the cache, then release the semaphore.</item>
    /// </list>
    /// Callers 2–N that were queued on the semaphore all hit the cache in step 3 and
    /// return without making any additional HTTP calls.
    /// </para>
    /// </remarks>
    public async Task<string> GetTokenAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default)
    {
        var cacheKey = CacheKeyFor(repository);

        // ── Fast path: token is already cached ───────────────────────────────
        if (_cache.TryGetValue(cacheKey, out string? cachedToken) && cachedToken is not null)
            return cachedToken;

        // ── Slow path: acquire per-key lock to serialise acquisition ─────────
        // GetOrAdd is atomic — every concurrent caller for the same key gets the
        // same SemaphoreSlim instance, so only ONE token POST is ever in flight.
        var sem = _keyLocks.GetOrAdd(cacheKey, static _ => new SemaphoreSlim(1, 1));

        await sem.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // ── Double-check: another caller may have set the cache while we waited
            if (_cache.TryGetValue(cacheKey, out cachedToken) && cachedToken is not null)
            {
                _logger.LogDebug(
                    "Token cache hit (after lock) for repository {Key}. Reusing token acquired by concurrent request.",
                    repository.Key);
                return cachedToken;
            }

            // ── We are the sole caller acquiring a token for this key right now
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

            var tokenResponse = await RequestTokenAsync(
                    tokenUrl, credentials.Username, credentials.Password, cancellationToken)
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
        finally
        {
            sem.Release();
        }
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

        _logger.LogInformation(
            "[LF AUTH] Login attempt: TokenUrl={TokenUrl} Repository={RepoId} User={Username} " +
            "Server={ServerUrl} ApiBase={ApiBasePath} Version={ApiVersion} " +
            "ContentType=application/x-www-form-urlencoded FormFields=[grant_type,username,password]",
            tokenUrl,
            repository.RepositoryId,
            username,
            _options.ServerUrl,
            _options.ApiBasePath,
            _options.EffectiveApiVersion);

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
            _logger.LogInformation(
                "[LF AUTH] Login failed for repository {RepoId}: HTTP {StatusCode}.",
                repository.RepositoryId,
                ex.StatusCode);
            return false;
        }
        // 404 (unknown repository), other 4xx, network failures and 5xx errors
        // propagate to the caller so it can show a precise error message.
    }

    /// <summary>
    /// Posts a password-grant token request to the Laserfiche <c>/Token</c> endpoint
    /// and deserialises the response.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Retries up to <see cref="MaxTokenRetries"/> times on HTTP 429, honouring the
    /// <c>Retry-After</c> header when present and applying exponential back-off
    /// (1 s, 2 s) otherwise.  All other error statuses are not retried.
    /// </para>
    /// <para>
    /// The password is never logged.
    /// </para>
    /// </remarks>
    private async Task<TokenResponse> RequestTokenAsync(
        string tokenUrl,
        string username,
        string password,
        CancellationToken cancellationToken)
    {
        // Log effective configuration once before the first attempt so administrators
        // can verify the exact URL and API contract being used — never log the password.
        _logger.LogInformation(
            "[LF AUTH] Token POST: Url={TokenUrl} " +
            "Server={ServerUrl} ApiBase={ApiBasePath} Version={ApiVersion} " +
            "ContentType=application/x-www-form-urlencoded " +
            "FormFields=[grant_type, username, password]",
            tokenUrl,
            _options.ServerUrl,
            _options.ApiBasePath,
            _options.EffectiveApiVersion);

        for (int attempt = 0; attempt <= MaxTokenRetries; attempt++)
        {
            if (attempt > 0)
            {
                _logger.LogInformation(
                    "[LF AUTH] Token POST retry {Attempt}/{MaxRetries}: Url={TokenUrl}",
                    attempt, MaxTokenRetries, tokenUrl);
            }

            using var client = _httpClientFactory.CreateClient("LaserficheRaw");

            var form = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"]   = username,
                ["password"]   = password
            });

            var sw = Stopwatch.StartNew();
            using var response = await client
                .PostAsync(tokenUrl, form, cancellationToken)
                .ConfigureAwait(false);
            sw.Stop();

            // ── HTTP 429 — rate limited ───────────────────────────────────────
            // Retry up to MaxTokenRetries times with Retry-After / exponential back-off.
            // This should rarely trigger after the single-flight fix eliminates the storm.
            if (response.StatusCode == HttpStatusCode.TooManyRequests && attempt < MaxTokenRetries)
            {
                var delay = ComputeRetryDelay(response, attempt);
                _logger.LogWarning(
                    "[LF AUTH] Token POST returned 429 Too Many Requests (attempt {Attempt}/{Max}). " +
                    "Retry-After header: {RetryAfter}. Waiting {DelayMs}ms before retry.",
                    attempt + 1, MaxTokenRetries + 1,
                    response.Headers.RetryAfter?.Delta?.ToString() ?? "not set",
                    (int)delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                continue; // response disposed by using block; start next iteration
            }

            // ── Non-success (including 429 after retries exhausted) ───────────
            var body = await response.Content
                .ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var diagId    = GenerateDiagnosticId();
                var sanitized = SanitizeBody(body);
                var lfCode    = TryExtractLFErrorCode(body);

                _logger.LogError(
                    "[LF AUTH] [DiagID:{DiagId}] Token request FAILED: " +
                    "HTTP {StatusCode} {ReasonPhrase} from {TokenUrl} ({DurationMs}ms). " +
                    "Server={ServerUrl} ApiBase={ApiBasePath} Version={ApiVersion}. " +
                    "Laserfiche response body (sanitized): {Body}",
                    diagId,
                    (int)response.StatusCode,
                    response.ReasonPhrase,
                    tokenUrl,
                    sw.ElapsedMilliseconds,
                    _options.ServerUrl,
                    _options.ApiBasePath,
                    _options.EffectiveApiVersion,
                    sanitized);

                throw new Domain.Exceptions.LaserficheException(
                    $"Laserfiche API returned HTTP {(int)response.StatusCode}.",
                    (int)response.StatusCode,
                    lfCode,
                    sanitized,
                    diagId);
            }

            _logger.LogDebug(
                "[LF AUTH] Token POST succeeded: HTTP {StatusCode} from {TokenUrl} ({DurationMs}ms).",
                (int)response.StatusCode, tokenUrl, sw.ElapsedMilliseconds);

            var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions.Default);

            if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
            {
                var diagId    = GenerateDiagnosticId();
                var sanitized = SanitizeBody(body);
                _logger.LogError(
                    "[LF AUTH] [DiagID:{DiagId}] Token response from {Url} was empty or malformed " +
                    "({DurationMs}ms). Body: {Body}",
                    diagId, tokenUrl, sw.ElapsedMilliseconds, sanitized);
                throw new Domain.Exceptions.LaserficheException(
                    "The Laserfiche API Server returned an empty token response. " +
                    "Ensure the API Server is running and the repository ID is correct.",
                    (int)response.StatusCode,
                    null,
                    sanitized,
                    diagId);
            }

            return tokenResponse;
        }

        // Unreachable: the loop either returns or throws inside the body.
        throw new InvalidOperationException("RequestTokenAsync: retry loop exited without result.");
    }

    /// <summary>
    /// Computes the delay before the next retry attempt.
    /// Honours the <c>Retry-After</c> header (delta-seconds form) when present
    /// and reasonable (≤ 30 s); otherwise falls back to exponential back-off:
    /// attempt 0 → 1 s, attempt 1 → 2 s.
    /// </summary>
    private static TimeSpan ComputeRetryDelay(HttpResponseMessage response, int attempt)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta && delta > TimeSpan.Zero)
        {
            // Cap at 30 s to avoid blocking the request pipeline indefinitely.
            return delta <= TimeSpan.FromSeconds(30) ? delta : TimeSpan.FromSeconds(30);
        }

        // Exponential back-off: 1 s, 2 s
        return TimeSpan.FromSeconds(Math.Pow(2, attempt));
    }

    // ──────────────────────────── Authorization Code exchange ────────────────

    /// <inheritdoc />
    public async Task<bool> ExchangeAuthorizationCodeAsync(
        RepositoryDescriptor repository,
        string code,
        string codeVerifier,
        string redirectUri,
        string clientId,
        CancellationToken cancellationToken = default)
    {
        // Always exchange at the V2 endpoint — LFDS issues V2-format codes only.
        var tokenUrl = _adapter.BuildTokenUrlV2(repository.RepositoryId);

        _logger.LogInformation(
            "[LF AUTH][SSO] Authorization code exchange: TokenUrl={TokenUrl} Repository={RepoId} " +
            "ContentType=application/x-www-form-urlencoded " +
            "FormFields=[grant_type, code, code_verifier, redirect_uri, client_id]",
            tokenUrl,
            repository.RepositoryId);

        using var client = _httpClientFactory.CreateClient("LaserficheRaw");

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["code"]          = code,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"]  = redirectUri,
            ["client_id"]     = clientId
        });

        var sw = Stopwatch.StartNew();
        using var response = await client
            .PostAsync(tokenUrl, form, cancellationToken)
            .ConfigureAwait(false);
        sw.Stop();

        var body = await response.Content
            .ReadAsStringAsync(cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            var diagId    = GenerateDiagnosticId();
            var sanitized = SanitizeBody(body);
            var lfCode    = TryExtractLFErrorCode(body);

            _logger.LogError(
                "[LF AUTH][SSO] [DiagID:{DiagId}] Code exchange FAILED: " +
                "HTTP {StatusCode} {ReasonPhrase} from {TokenUrl} ({DurationMs}ms). Body: {Body}",
                diagId,
                (int)response.StatusCode,
                response.ReasonPhrase,
                tokenUrl,
                sw.ElapsedMilliseconds,
                sanitized);

            if ((int)response.StatusCode < 500)
                return false;

            throw new Domain.Exceptions.LaserficheException(
                $"LFDS token exchange returned HTTP {(int)response.StatusCode}.",
                (int)response.StatusCode,
                lfCode,
                sanitized,
                diagId);
        }

        _logger.LogDebug(
            "[LF AUTH][SSO] Code exchange HTTP {StatusCode} from {TokenUrl} ({DurationMs}ms).",
            (int)response.StatusCode, tokenUrl, sw.ElapsedMilliseconds);

        var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(body, JsonOptions.Default);

        if (tokenResponse is null || string.IsNullOrWhiteSpace(tokenResponse.AccessToken))
        {
            var diagId    = GenerateDiagnosticId();
            var sanitized = SanitizeBody(body);
            _logger.LogError(
                "[LF AUTH][SSO] [DiagID:{DiagId}] Empty/malformed token response from {Url} " +
                "({DurationMs}ms). Body: {Body}",
                diagId, tokenUrl, sw.ElapsedMilliseconds, sanitized);
            return false;
        }

        var cacheKey      = CacheKeyFor(repository);
        var expirySeconds = Math.Max(tokenResponse.ExpiresIn - EarlyExpiryBufferSeconds, 30);
        _cache.Set(cacheKey, tokenResponse.AccessToken, TimeSpan.FromSeconds(expirySeconds));

        _logger.LogInformation(
            "[LF AUTH][SSO] SSO token exchange succeeded for repository {RepoId}. " +
            "Cached for {CacheSeconds}s.",
            repository.RepositoryId,
            expirySeconds);

        return true;
    }

    // ──────────────────────────── Diagnostic helpers ─────────────────────────

    private static string GenerateDiagnosticId() =>
        Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();

    private static string SanitizeBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(empty)";
        if (body.Length > 2000)
            body = body[..2000] + "...[truncated]";

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return body;

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    var isSensitive =
                        prop.Name.Equals("access_token",  StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("refresh_token", StringComparison.OrdinalIgnoreCase) ||
                        prop.Name.Equals("password",      StringComparison.OrdinalIgnoreCase);
                    if (isSensitive)
                        writer.WriteString(prop.Name, "[REDACTED]");
                    else
                        prop.WriteTo(writer);
                }
                writer.WriteEndObject();
            }
            return Encoding.UTF8.GetString(stream.ToArray());
        }
        catch (JsonException)
        {
            return body;
        }
    }

    private static string? TryExtractLFErrorCode(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

            if (doc.RootElement.TryGetProperty("errorCode", out var ec))
                return ec.ValueKind == JsonValueKind.Number
                    ? ec.GetInt32().ToString()
                    : ec.GetString();

            if (doc.RootElement.TryGetProperty("code", out var c))
                return c.GetString();

            if (doc.RootElement.TryGetProperty("title", out var t))
                return t.GetString();
        }
        catch (JsonException) { }
        return null;
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
