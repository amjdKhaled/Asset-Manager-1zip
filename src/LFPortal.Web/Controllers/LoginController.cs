using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Exceptions;
using LFPortal.Infrastructure.OAuth;
using LFPortal.Infrastructure.Options;
using LFPortal.Web.Middleware;
using LFPortal.Web.Authentication;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Handles sign-in for the Dashboard portal.
/// </summary>
/// <remarks>
/// <para>
/// <b>Password-grant flow (primary):</b>  When LFDS SSO is not configured,
/// <c>GET /Login</c> renders a credential form.  A valid POST stores a DPAPI-encrypted
/// password in the session so subsequent token-refresh requests can acquire a new
/// Bearer token without re-prompting the user.
/// </para>
/// <para>
/// <b>LFDS OAuth2 Authorization Code flow (SSO):</b>  When <c>Laserfiche:Sso:LfdsBaseUrl</c>
/// is configured, <c>GET /Login</c> transparently redirects to <c>/Login/StartSso</c>,
/// which initiates a PKCE Authorization Code exchange with LFDS.  The callback
/// <c>GET /Login/Callback</c> validates the response and caches the resulting Bearer token
/// before redirecting to the originally requested URL.  If SSO fails the browser returns
/// to the password form via <c>/Login?ssoFailed=true</c>.
/// </para>
/// <para>
/// <c>GET /Login/SignOut</c> invalidates cached tokens for the current session and
/// redirects to the Login page; it does <em>not</em> log the user out of LFDS.
/// </para>
/// </remarks>
public sealed class LoginController : Controller
{
    // Session key written here, read by the guard middleware.
    internal const string SessionKeyOAuthPendingState = "OAuth_PendingState";

    private readonly ILaserficheAuthService     _authService;
    private readonly IRepositoryContext         _repositoryContext;
    private readonly ISessionCredentialStore    _sessionCredentialStore;
    private readonly IOAuthStateStore           _oAuthStateStore;
    private readonly IOAuthTransactionCookie    _oAuthTransactionCookie;
    private readonly IOptionsMonitor<LaserficheOptions> _options;
    private readonly ILogger<LoginController>   _logger;

    /// <summary>Initialises the controller.</summary>
    public LoginController(
        ILaserficheAuthService       authService,
        IRepositoryContext           repositoryContext,
        ISessionCredentialStore      sessionCredentialStore,
        IOAuthStateStore             oAuthStateStore,
        IOAuthTransactionCookie      oAuthTransactionCookie,
        IOptionsMonitor<LaserficheOptions> options,
        ILogger<LoginController>     logger)
    {
        _authService             = authService;
        _repositoryContext       = repositoryContext;
        _sessionCredentialStore  = sessionCredentialStore;
        _oAuthStateStore         = oAuthStateStore;
        _oAuthTransactionCookie  = oAuthTransactionCookie;
        _options                 = options;
        _logger                  = logger;
    }

    // ------------------------------------------------------------------ //
    // GET /Login                                                           //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Renders the sign-in form, or transparently redirects to LFDS SSO when
    /// <c>Laserfiche:Sso:LfdsBaseUrl</c> is configured.
    /// Pass <c>?ssoFailed=true</c> to suppress the SSO redirect and display the form.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        bool   ssoFailed         = false,
        string? ssoFailure       = null,
        string? returnUrl        = null,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;

        // ── SSO fast-path ─────────────────────────────────────────────────────
        // When LFDS is configured and SSO has not already failed this session,
        // redirect transparently so the credential form is never shown.
        if (opts.Sso.IsConfigured && !ssoFailed)
        {
            _logger.LogInformation("[SSO] LFDS configured — redirecting to StartSso.");
            return RedirectToAction("StartSso", new { returnUrl });
        }

        // ── Password-grant form ───────────────────────────────────────────────
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        var vm   = new LoginViewModel
        {
            ActiveRepository     = repo.RepositoryId,
            AllowRepositoryInput = AllowRepositoryInput(),
            SubmittedRepository  = repo.RepositoryId,
            SsoFailed            = ssoFailed && opts.Sso.IsConfigured,
            SsoFailureReason     = ssoFailed && opts.Sso.IsConfigured
                ? DescribeSsoFailure(ssoFailure)
                : null,
        };
        return View(vm);
    }

    // ------------------------------------------------------------------ //
    // POST /Login                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Validates the submitted credentials against the active Laserfiche repository
    /// and, on success, establishes the session authentication state.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(
        LoginInputModel   input,
        CancellationToken cancellationToken)
    {
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        var allowRepoInput = AllowRepositoryInput();

        var repoId = allowRepoInput && !string.IsNullOrWhiteSpace(input.Repository)
            ? input.Repository.Trim()
            : repo.RepositoryId;

        LoginViewModel ViewWithError(string? error) => new()
        {
            ActiveRepository     = repo.RepositoryId,
            AllowRepositoryInput = allowRepoInput,
            SubmittedRepository  = allowRepoInput ? (input.Repository ?? string.Empty) : repo.RepositoryId,
            SubmittedUsername    = input.Username,
            ErrorMessage         = error
        };

        if (!ModelState.IsValid)
            return View(ViewWithError(null));

        if (string.IsNullOrWhiteSpace(repoId))
        {
            return View(ViewWithError(
                "Enter the name of the Laserfiche repository to sign in to."));
        }

        var targetRepo = repo with { RepositoryId = repoId, DisplayName = repoId };

        _logger.LogInformation(
            "Login: authenticating user {Username} for repository {RepoId}.",
            input.Username, repoId);

        bool success;
        try
        {
            success = await _authService.TryAuthenticateAsync(
                targetRepo,
                input.Username,
                input.Password ?? string.Empty,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Login: error while authenticating for repository {RepoId}: {ErrorType} " +
                "(HResult=0x{HResult:X8}). ServerUrl={ServerUrl} Host={Host}.",
                repoId, ex.GetType().Name, ex.HResult,
                targetRepo.ServerUrl,
                Uri.TryCreate(targetRepo.ServerUrl, UriKind.Absolute, out var su) ? su.Host : "(invalid)");

            var classification = Diagnostics.LaserficheErrorClassifier.Classify(ex);
            if (classification.Code == "tls-error" &&
                Uri.TryCreate(targetRepo.ServerUrl, UriKind.Absolute, out var serverUri))
            {
                await Diagnostics.TlsCertificateInspector.InspectAndLogAsync(
                    serverUri, _logger, cancellationToken);
            }

            return View(ViewWithError(ClassifyLoginError(ex, repoId)));
        }

        if (!success)
        {
            _logger.LogInformation(
                "Login: invalid credentials submitted for repository {RepoId}.", repoId);
            return View(ViewWithError(
                $"Unable to sign in to {repoId}. Check the username and password."));
        }

        // ── Authentication succeeded ──────────────────────────────────────────

        await _sessionCredentialStore.StoreAsync(
            input.Username,
            input.Password ?? string.Empty,
            cancellationToken);

        HttpContext.Session.SetString(
            RepositorySessionMiddleware.SessionKeyRepositoryId,
            repoId);
        HttpContext.Session.SetString(
            SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId,
            repoId);

        await EstablishDashboardIdentityAsync(
            input.Username,
            repoId,
            DashboardAuthenticationDefaults.PasswordAuthenticationMethod);

        _logger.LogInformation(
            "Login: session authenticated for repository {RepoId}.", repoId);

        return RedirectToAction("Index", "Dashboard");
    }

    // ------------------------------------------------------------------ //
    // GET /Login/StartSso                                                  //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Initiates the LFDS OAuth2 Authorization Code + PKCE flow.
    /// Generates a cryptographically random <c>state</c> and PKCE pair, stores them
    /// server-side, writes the state to the session (CSRF binding), then redirects the
    /// browser to the Repository API authorization endpoint. The Repository API
    /// performs the LFDS/WebSTS interaction.
    /// </summary>
    [HttpGet("/Login/StartSso")]
    public async Task<IActionResult> StartSso(
        string? returnUrl        = null,
        CancellationToken cancellationToken = default)
    {
        var opts = _options.CurrentValue;

        if (!opts.Sso.IsConfigured)
        {
            _logger.LogWarning("[SSO] StartSso called but LFDS is not configured. Falling back to Login.");
            return RedirectToAction("Index", "Login");
        }

        var invalidKeys = opts.MarkdownConfigurationKeys();
        if (invalidKeys.Count > 0 || string.IsNullOrWhiteSpace(opts.SsoCallbackUrl))
        {
            var detail = invalidKeys.Count > 0
                ? "Invalid Markdown URL configuration: " + string.Join(", ", invalidKeys)
                : "Laserfiche:DashboardPublicBaseUrl is required for SSO.";
            _logger.LogCritical("[SSO] Cannot start SSO. {ConfigurationError}", detail);
            return RedirectToAction("Diagnostic", new { reason = "configuration_error", detail });
        }

        if (!string.IsNullOrWhiteSpace(opts.Sso.RedirectUri) &&
            !string.Equals(opts.Sso.RedirectUri.TrimEnd('/'), opts.SsoCallbackUrl,
                StringComparison.OrdinalIgnoreCase))
        {
            var detail = $"Redirect URI mismatch: Dashboard generated {opts.SsoCallbackUrl}, " +
                $"but configured expected callback is {opts.Sso.RedirectUri.TrimEnd('/')}. " +
                $"Laserfiche API must whitelist exactly {opts.SsoCallbackUrl}.";
            _logger.LogError("[SSO] {ConfigurationError}", detail);
            return RedirectToAction("Diagnostic", new { reason = "redirect_uri_mismatch", detail });
        }

        // Validate returnUrl — anti-open-redirect.
        if (!IsLocalUrl(returnUrl))
            returnUrl = Url.Action("Index", "Dashboard")!;

        // Resolve active repository (populated by RepositorySessionMiddleware).
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);

        // ── PKCE ─────────────────────────────────────────────────────────────
        var codeVerifier  = GenerateCryptoRandomBase64Url(byteCount: 64);  // 64 bytes → 86-char verifier
        var codeChallenge = ComputeS256Challenge(codeVerifier);

        // ── State (CSRF protection) ───────────────────────────────────────────
        var state = GenerateCryptoRandomBase64Url(byteCount: 32);          // 32 bytes → 43-char state

        // ── Redirect URI ──────────────────────────────────────────────────────
        var redirectUri = BuildCallbackUri(opts);

        // ── Server-side state entry ───────────────────────────────────────────
        var entry = new OAuthStateEntry
        {
            RepositoryId = repo.RepositoryId,
            ReturnUrl    = returnUrl ?? "/",
            CodeVerifier = codeVerifier,
            RedirectUri  = redirectUri,
            ExpiresAt    = DateTimeOffset.UtcNow.AddMinutes(10),
        };
        _oAuthStateStore.Store(state, entry);

        var launchSource = HttpContext.Session.GetString(
            RepositorySessionMiddleware.SessionKeySource);
        var cookieWrite = _oAuthTransactionCookie.Write(HttpContext, new OAuthTransaction(
            state, codeVerifier, repo.RepositoryId, returnUrl ?? "/",
            DateTimeOffset.UtcNow, launchSource, redirectUri));

        // Bind state to this session so the callback can validate CSRF.
        HttpContext.Session.SetString(SessionKeyOAuthPendingState, state);

        _logger.LogInformation(
            "[SSO] OAuth correlation cookie. State={State}; CookieName={CookieName}; " +
            "CookieWritten={CookieWritten}; SameSite={SameSite}; Secure={Secure}; Path={Path}; " +
            "ExpiresUtc={ExpiresUtc:o}; Repository={Repository}; CallbackUrl={CallbackUrl}; " +
            "ReturnUrl={ReturnUrl}.",
            StateForLog(state),
            cookieWrite.CookieName,
            cookieWrite.Written,
            cookieWrite.SameSite,
            cookieWrite.Secure,
            cookieWrite.Path,
            cookieWrite.ExpiresUtc,
            repo.RepositoryId,
            redirectUri,
            returnUrl);

        if (!cookieWrite.Written)
        {
            _logger.LogError("[SSO] Correlation Set-Cookie header was not emitted; aborting authorization.");
            return RedirectToSsoDiagnostic("oauth_correlation_cookie_missing");
        }

        // Build the authorize URL exactly once; the same value is logged and redirected.
        var authorizeUrl = BuildAuthorizationUrl(opts, state, codeChallenge, redirectUri);

        _logger.LogInformation(
            "[SSO] Redirecting to Repository API authorize URL: {AuthorizeUrl} " +
            "(Repo={Repo}, RedirectUri={RedirectUri})",
            authorizeUrl,
            repo.RepositoryId,
            redirectUri);

        return Redirect(authorizeUrl);
    }

    // ------------------------------------------------------------------ //
    // GET /Login/Callback                                                  //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Handles the LFDS authorization callback.
    /// Validates the state (CSRF + anti-replay), exchanges the authorization code for
    /// a Bearer token via PKCE, marks the session as authenticated, and redirects to
    /// the originally requested URL.
    /// </summary>
    [HttpGet("/Login/Callback")]
    public async Task<IActionResult> Callback(
        string? code             = null,
        string? state            = null,
        string? error            = null,
        string? errorDescription = null,
        CancellationToken cancellationToken = default)
    {
        var queryKeys = HttpContext.Request.Query.Keys
            .OrderBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _logger.LogInformation(
            "[SSO] Callback received. QueryKeys={QueryKeys}; CodePresent={CodePresent}; " +
            "State={State}; SessionAvailable={SessionAvailable}; SessionId={SessionId}.",
            queryKeys,
            !string.IsNullOrWhiteSpace(code),
            StateForLog(state),
            HttpContext.Session.IsAvailable,
            HttpContext.Session.Id);

        // ── LFDS returned an error ────────────────────────────────────────────
        if (!string.IsNullOrEmpty(error))
        {
            _logger.LogWarning(
                "[SSO] LFDS returned error in callback: {Error} — {Description}",
                error,
                errorDescription ?? "(no description)");
            return FallBackToLoginForm("provider_error", error);
        }

        // ── Code and state must both be present ───────────────────────────────
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
        {
            var reason = string.IsNullOrEmpty(code) ? "missing_code" : "missing_state";
            _logger.LogWarning("[SSO] Callback rejected: {FailureReason}.", reason);
            return FallBackToLoginForm(reason);
        }

        // The protected correlation cookie is authoritative across the LFDS round
        // trip. ASP.NET Session remains a compatibility fallback, not a dependency.
        var sessionState = HttpContext.Session.GetString(SessionKeyOAuthPendingState);
        var cookieResult = _oAuthTransactionCookie.Read(HttpContext);
        OAuthStateEntry? entry = null;

        if (cookieResult.IsValid && cookieResult.Transaction is { } transaction)
        {
            if (!CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(transaction.State), Encoding.UTF8.GetBytes(state)))
                return RedirectToSsoDiagnostic("oauth_state_mismatch", transaction);

            entry = new OAuthStateEntry
            {
                RepositoryId = transaction.RepositoryId,
                ReturnUrl = transaction.ReturnUrl,
                CodeVerifier = transaction.CodeVerifier,
                RedirectUri = transaction.RedirectUri,
                ExpiresAt = transaction.CreatedAtUtc.AddMinutes(10),
            };
        }
        else if (!string.IsNullOrWhiteSpace(sessionState) &&
                 string.Equals(sessionState, state, StringComparison.Ordinal))
        {
            entry = _oAuthStateStore.TryConsume(state);
        }
        else
        {
            return RedirectToSsoDiagnostic(cookieResult.CookiePresent
                ? "oauth_correlation_cookie_invalid"
                : "oauth_correlation_cookie_missing");
        }

        _logger.LogInformation(
            "[SSO] Correlation lookup completed. State={State}; CookiePresent={CookiePresent}; " +
            "CookieValid={CookieValid}; Found={StateFound}; PkceVerifierPresent={PkceVerifierPresent}; " +
            "StoredRepository={StoredRepository}.",
            StateForLog(state),
            cookieResult.CookiePresent,
            cookieResult.IsValid,
            entry is not null,
            !string.IsNullOrWhiteSpace(entry?.CodeVerifier),
            entry?.RepositoryId ?? "(none)");
        if (entry is null)
        {
            // OAuthStateStore already logged the reason (expired / replay / unknown).
            return RedirectToSsoDiagnostic("oauth_correlation_cookie_missing");
        }

        if (string.IsNullOrWhiteSpace(entry.CodeVerifier))
        {
            _logger.LogError("[SSO] Callback rejected: stored PKCE verifier is missing.");
            return RedirectToSsoDiagnostic("pkce_verifier_missing", cookieResult.Transaction);
        }
        if (string.IsNullOrWhiteSpace(entry.RepositoryId))
            return RedirectToSsoDiagnostic("oauth_repository_missing", cookieResult.Transaction);

        // Clear pending state from session — successfully retrieved from store.
        HttpContext.Session.Remove(SessionKeyOAuthPendingState);

        // ── Repository consistency check ──────────────────────────────────────
        var activeRepo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        var repo = activeRepo with
        {
            RepositoryId = entry.RepositoryId,
            DisplayName = entry.RepositoryId,
        };

        if (!string.IsNullOrEmpty(sessionState) && !string.IsNullOrEmpty(entry.RepositoryId) &&
            !string.Equals(activeRepo.RepositoryId, entry.RepositoryId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "[SSO] Repository mismatch: state={StateRepo} active={ActiveRepo}. " +
                "Browser repository may have changed during the SSO flow.",
                entry.RepositoryId,
                repo.RepositoryId);
            return RedirectToSsoDiagnostic("repository_mismatch", cookieResult.Transaction);
        }

        var opts = _options.CurrentValue;

        // ── Token exchange ────────────────────────────────────────────────────
        bool success;
        try
        {
            _logger.LogInformation(
                "[SSO] Exchanging authorization code. Repository={Repository}; " +
                "TokenEndpoint={TokenEndpoint}; PkceVerifierPresent={PkceVerifierPresent}.",
                repo.RepositoryId,
                BuildTokenEndpointForLog(opts, repo.RepositoryId),
                true);
            success = await _authService.ExchangeAuthorizationCodeAsync(
                repo,
                code,
                entry.CodeVerifier,
                entry.RedirectUri,
                opts.Sso.ClientId,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "[SSO] Token exchange threw an exception for repository {Repo}.",
                repo.RepositoryId);
            if (IsUntrustedSamlToken(ex))
            {
                return RedirectToAction("Diagnostic", new
                {
                    reason = "saml_token_untrusted",
                    repositoryId = repo.RepositoryId,
                    tokenEndpoint = opts.GetSsoTokenEndpoint(repo.RepositoryId),
                    callbackUrl = entry.RedirectUri,
                });
            }
            return RedirectToSsoDiagnostic(
                "token_exchange_failed",
                cookieResult.Transaction,
                repo.RepositoryId,
                opts.GetSsoTokenEndpoint(repo.RepositoryId));
        }

        if (!success)
        {
            _logger.LogWarning(
                "[SSO] Token exchange returned failure for repository {Repo}. " +
                "Falling back to Login form.",
                repo.RepositoryId);
            return RedirectToSsoDiagnostic(
                "token_exchange_failed",
                cookieResult.Transaction,
                repo.RepositoryId,
                opts.GetSsoTokenEndpoint(repo.RepositoryId));
        }

        // ── Mark session as authenticated ─────────────────────────────────────
        HttpContext.Session.SetString(
            RepositorySessionMiddleware.SessionKeyRepositoryId,
            repo.RepositoryId);
        HttpContext.Session.SetString(
            SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId,
            repo.RepositoryId);

        _logger.LogInformation(
            "[SSO] Repository session markers stored. ActiveRepositorySet={ActiveRepositorySet}; " +
            "AuthenticatedRepositorySet={AuthenticatedRepositorySet}; Repository={Repository}.",
            HttpContext.Session.GetString(RepositorySessionMiddleware.SessionKeyRepositoryId) is not null,
            HttpContext.Session.GetString(SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId) is not null,
            repo.RepositoryId);

        _logger.LogInformation("[SSO] Calling SignInAsync for repository {Repository}.", repo.RepositoryId);
        await EstablishDashboardIdentityAsync(
            identityName: null,
            repositoryId: repo.RepositoryId,
            authenticationMethod: DashboardAuthenticationDefaults.LfdsAuthenticationMethod);
        _oAuthTransactionCookie.Delete(HttpContext);

        _logger.LogInformation(
            "[SSO] Session authenticated via LFDS for repository {Repo}.",
            repo.RepositoryId);

        // ── Redirect to original destination ──────────────────────────────────
        var returnUrl = entry.ReturnUrl;
        if (!IsLocalUrl(returnUrl))
            returnUrl = Url.Action("Index", "Dashboard")!;

        _logger.LogInformation(
            "[SSO] Callback completed. Access token cached by exchange service; " +
            "SignInAsync completed; RedirectTarget={RedirectTarget}.",
            returnUrl);

        return LocalRedirect(returnUrl);
    }

    // ------------------------------------------------------------------ //
    // Error classification                                                 //
    // ------------------------------------------------------------------ //

    private static string ClassifyLoginError(Exception ex, string repoId)
    {
        if (ex is LaserficheException lex)
        {
            if (lex.StatusCode == 404)
                return $"Repository \"{repoId}\" was not found on the Laserfiche server. " +
                       "Check the repository name (it is case-sensitive).";

            if (lex.StatusCode >= 500)
            {
                var diagPart = lex.DiagnosticId is not null
                    ? $" Diagnostic ID: {lex.DiagnosticId}."
                    : string.Empty;
                return $"Authentication failed. Laserfiche API returned HTTP {lex.StatusCode}.{diagPart} " +
                       "Check server logs or contact your Laserfiche administrator.";
            }

            return $"The Laserfiche server rejected the request (HTTP {lex.StatusCode}).";
        }

        return Diagnostics.LaserficheErrorClassifier.Classify(ex).Detail;
    }

    // ------------------------------------------------------------------ //
    // GET /Login/SignOut                                                   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Clears the session authentication state and cached tokens, then redirects to the
    /// Login page.  Does <em>not</em> log the user out of LFDS or Laserfiche.
    /// </summary>
    [HttpGet("/Login/SignOut")]
    public async Task<IActionResult> SignOut(CancellationToken cancellationToken)
    {
        await _authService.InvalidateCurrentSessionTokensAsync();

        await HttpContext.SignOutAsync(DashboardAuthenticationDefaults.Scheme);
        _oAuthTransactionCookie.Delete(HttpContext);

        HttpContext.Session.Remove(SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId);
        HttpContext.Session.Remove(SessionKeyOAuthPendingState);
        await _sessionCredentialStore.ClearAsync(cancellationToken);

        _logger.LogInformation("Login: session authentication cleared (Change Account).");

        return RedirectToAction("Index", "Login");
    }

    // ------------------------------------------------------------------ //
    // Private helpers                                                      //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Persists the Dashboard identity established by a successful Laserfiche
    /// password or LFDS authorization-code exchange.
    /// </summary>
    private Task EstablishDashboardIdentityAsync(
        string? identityName,
        string repositoryId,
        string authenticationMethod)
    {
        var claims = new List<Claim>
        {
            new Claim(DashboardAuthenticationDefaults.RepositoryClaimType, repositoryId),
            new Claim(ClaimTypes.AuthenticationMethod, authenticationMethod),
        };

        // The password flow knows the submitted username. The LFDS token response
        // currently does not expose a verified username claim, so do not invent one.
        if (!string.IsNullOrWhiteSpace(identityName))
            claims.Add(new Claim(ClaimTypes.Name, identityName));

        var identity  = new ClaimsIdentity(claims, DashboardAuthenticationDefaults.Scheme);
        var principal = new ClaimsPrincipal(identity);

        return HttpContext.SignInAsync(
            DashboardAuthenticationDefaults.Scheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = true,
                ExpiresUtc   = DateTimeOffset.UtcNow.AddHours(8),
            });
    }

    /// <summary>
    /// Repository selection is allowed only for direct browser sessions.
    /// Desktop / Web Client sessions carry their repository in the launch URL.
    /// </summary>
    private bool AllowRepositoryInput()
    {
        var source = HttpContext.Session.GetString(
            RepositorySessionMiddleware.SessionKeySource);
        return string.IsNullOrEmpty(source);
    }

    /// <summary>
    /// Returns the deterministic OAuth2 redirect URI derived from DashboardPublicBaseUrl.
    /// </summary>
    private string BuildCallbackUri(LaserficheOptions opts)
    {
        return opts.SsoCallbackUrl;
    }

    private static bool IsUntrustedSamlToken(Exception exception) =>
        exception is LaserficheException { StatusCode: 403 } laserfiche &&
        (laserfiche.ResponseBody?.Contains("invalid or untrusted SAML token",
             StringComparison.OrdinalIgnoreCase) == true ||
         string.Equals(laserfiche.LFErrorCode, "9530", StringComparison.OrdinalIgnoreCase));

    private RedirectToActionResult RedirectToSsoDiagnostic(
        string reason,
        OAuthTransaction? transaction = null,
        string? repositoryId = null,
        string? tokenEndpoint = null)
    {
        _logger.LogWarning(
            "[SSO] Redirecting to diagnostic page. FailureReason={FailureReason}; Repository={Repository}.",
            reason,
            repositoryId ?? transaction?.RepositoryId ?? "(unknown)");
        return RedirectToAction("Diagnostic", new
        {
            reason,
            repositoryId = repositoryId ?? transaction?.RepositoryId,
            tokenEndpoint,
            callbackUrl = transaction?.RedirectUri,
        });
    }

    /// <summary>Displays actionable SSO/configuration failures without password fallback.</summary>
    [HttpGet("/Login/SsoDiagnostic")]
    public IActionResult Diagnostic(
        string reason,
        string? detail = null,
        string? repositoryId = null,
        string? tokenEndpoint = null,
        string? callbackUrl = null)
    {
        var opts = _options.CurrentValue;
        return View("SsoDiagnostic", new SsoDiagnosticViewModel
        {
            Reason = reason,
            Detail = detail ?? DescribeSsoDiagnostic(reason),
            RepositoryId = repositoryId ?? opts.RepositoryId,
            TokenEndpoint = tokenEndpoint ?? opts.GetSsoTokenEndpoint(repositoryId ?? opts.RepositoryId),
            CallbackUrl = callbackUrl ?? opts.SsoCallbackUrl,
        });
    }

    private static string DescribeSsoDiagnostic(string reason) => reason switch
    {
        "oauth_correlation_cookie_missing" => "Dashboard did not receive the OAuth correlation cookie after LFDS returned. Check browser cookie policy, host mismatch, or IIS/app restart.",
        "oauth_correlation_cookie_invalid" => "Dashboard received the OAuth correlation cookie, but it was invalid or expired.",
        "oauth_state_mismatch" => "The OAuth state returned by LFDS did not match the protected Dashboard transaction.",
        "pkce_verifier_missing" => "The protected OAuth transaction did not contain the PKCE verifier required for token exchange.",
        "token_exchange_failed" => "LFDS returned to Dashboard, but Repository API rejected the authorization-code exchange.",
        "repository_mismatch" => "The repository changed during the LFDS authentication transaction.",
        "oauth_repository_missing" => "The protected OAuth transaction did not contain a repository identifier.",
        _ => "The LFDS OAuth transaction could not be completed.",
    };

    /// <summary>Redirects to the Login form with the SSO-failed flag set.</summary>
    private RedirectToActionResult FallBackToLoginForm(
        string reason,
        string? detail = null)
    {
        _logger.LogWarning(
            "[SSO] Falling back to Login. FailureReason={FailureReason}; Detail={Detail}.",
            reason,
            detail ?? "(none)");
        return RedirectToAction("Index", "Login", new { ssoFailed = true, ssoFailure = reason });
    }

    private static string StateForLog(string? state) =>
        string.IsNullOrWhiteSpace(state)
            ? "(missing)"
            : state.Length > 8 ? state[..8] + "…" : state;

    private static string BuildTokenEndpointForLog(LaserficheOptions opts, string repositoryId) =>
        opts.GetSsoTokenEndpoint(repositoryId);

    private static string DescribeSsoFailure(string? reason) => reason switch
    {
        "provider_error"        => "Laserfiche returned an authorization error.",
        "missing_code"          => "The callback did not contain an authorization code.",
        "missing_state"         => "The callback did not contain OAuth state.",
        "oauth_correlation_cookie_missing" => "Dashboard did not receive the OAuth correlation cookie after LFDS returned. Check browser cookie policy, host mismatch, or IIS/app restart.",
        "oauth_correlation_cookie_invalid" => "The OAuth correlation cookie could not be decrypted or has expired.",
        "oauth_state_mismatch"   => "The returned OAuth state did not match the protected Dashboard transaction.",
        "pkce_verifier_missing"  => "The protected OAuth transaction did not contain its PKCE verifier.",
        "oauth_repository_missing" => "The protected OAuth transaction did not identify a repository.",
        "repository_mismatch"   => "The active repository changed during authentication.",
        "token_exchange_failed" => "Repository API rejected the authorization-code exchange.",
        "callback_exception"    => "An unexpected error occurred during the OAuth callback.",
        _                       => "The OAuth callback could not be completed.",
    };

    /// <summary>Returns true when the URL is local to this application (anti-open-redirect).</summary>
    private bool IsLocalUrl(string? url) =>
        !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url);

    /// <summary>Builds the Repository API authorization URL with all required PKCE parameters.</summary>
    private static string BuildAuthorizationUrl(
        LaserficheOptions opts,
        string            state,
        string            codeChallenge,
        string            redirectUri)
    {
        var endpoint = opts.SsoAuthorizationEndpoint;

        var query = new StringBuilder();
        query.Append("response_type=code");
        query.Append("&redirect_uri=");     query.Append(Uri.EscapeDataString(redirectUri));
        query.Append("&state=");            query.Append(Uri.EscapeDataString(state));
        query.Append("&code_challenge=");   query.Append(Uri.EscapeDataString(codeChallenge));
        query.Append("&code_challenge_method=S256");

        return $"{endpoint}?{query}";
    }

    // ── PKCE helpers ──────────────────────────────────────────────────────────

    /// <summary>Generates a cryptographically random base64url-encoded string.</summary>
    private static string GenerateCryptoRandomBase64Url(int byteCount)
    {
        var bytes = RandomNumberGenerator.GetBytes(byteCount);
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Computes the PKCE S256 code challenge from a code verifier:
    /// <c>BASE64URL(SHA-256(ASCII(code_verifier)))</c>.
    /// </summary>
    private static string ComputeS256Challenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    /// <summary>Encodes bytes as a URL-safe base64 string without padding.</summary>
    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes)
               .TrimEnd('=')
               .Replace('+', '-')
               .Replace('/', '_');
}

public sealed class SsoDiagnosticViewModel
{
    public string Reason { get; init; } = string.Empty;
    public string? Detail { get; init; }
    public string RepositoryId { get; init; } = string.Empty;
    public string TokenEndpoint { get; init; } = string.Empty;
    public string CallbackUrl { get; init; } = string.Empty;
}

// ── View models ──────────────────────────────────────────────────────────────

/// <summary>View model passed to the Login view.</summary>
public sealed class LoginViewModel
{
    /// <summary>Repository name displayed on the login form.</summary>
    public string ActiveRepository { get; init; } = string.Empty;

    /// <summary>
    /// True for direct browser sessions that may choose the repository on the form.
    /// False for Desktop / Web Client launches where the repository is fixed.
    /// </summary>
    public bool AllowRepositoryInput { get; init; }

    /// <summary>Preserves the repository name the user entered after a failed attempt.</summary>
    public string SubmittedRepository { get; init; } = string.Empty;

    /// <summary>Error message shown below the form after a failed sign-in attempt.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Preserves the username the user entered on a failed attempt.</summary>
    public string SubmittedUsername { get; init; } = string.Empty;

    /// <summary>
    /// True when SSO is configured but the SSO flow failed and the user has been
    /// redirected back to the password form as a fallback.
    /// </summary>
    public bool SsoFailed { get; init; }

    /// <summary>Sanitized reason for an unsuccessful SSO callback.</summary>
    public string? SsoFailureReason { get; init; }
}

/// <summary>Binds the login form POST payload.</summary>
public sealed class LoginInputModel
{
    /// <summary>Repository name for direct browser sessions.</summary>
    public string? Repository { get; set; }

    /// <summary>Laserfiche username. Required.</summary>
    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Laserfiche password. Optional — blank passwords are valid Laserfiche accounts.
    /// Do NOT add [Required] here.
    /// </summary>
    public string? Password { get; set; }
}
