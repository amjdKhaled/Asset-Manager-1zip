using LFPortal.Infrastructure.Options;
using LFPortal.Web.Authentication;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Middleware;

/// <summary>
/// Guards protected routes until the browser has completed Laserfiche authentication
/// for the currently active repository.
/// </summary>
/// <remarks>
/// <para>
/// Desktop Client sessions are always guarded. Web Client and direct-browser sessions
/// are also guarded when LFDS SSO is configured.
/// </para>
/// <para>
/// When LFDS SSO is dormant, Web Client and configured direct-browser sessions preserve
/// their legacy fallback behavior.
/// </para>
/// <para>
/// A guarded session is considered authenticated when <c>AuthenticatedRepositoryId</c>
/// matches <c>ActiveRepositoryId</c>.  Switching repositories causes
/// <c>ActiveRepositoryId</c> to change and the guard redirects to <c>/Login</c> again.
/// </para>
/// <para>
/// Excluded paths (never redirected):
/// <list type="bullet">
///   <item><c>/Login</c> and all sub-paths</item>
///   <item><c>/Launch</c></item>
///   <item><c>/Settings</c> and all sub-paths</item>
///   <item><c>/health</c></item>
///   <item><c>/Home</c> (error pages)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class SessionAuthGuardMiddleware
{
    internal const string SessionKeyAuthenticatedRepoId = "AuthenticatedRepositoryId";
    private  const string SessionKeyActiveRepoId        = "ActiveRepositoryId";
    private  const string SessionKeyActiveRepoSource    = "ActiveRepositorySource";

    /// <summary>
    /// Determines whether the current launch context requires authentication.
    /// </summary>
    private readonly RequestDelegate _next;
    private readonly IOptionsMonitor<LaserficheOptions> _options;
    private readonly ILogger<SessionAuthGuardMiddleware> _logger;

    /// <summary>Initialises the middleware.</summary>
    public SessionAuthGuardMiddleware(
        RequestDelegate next,
        IOptionsMonitor<LaserficheOptions> options,
        ILogger<SessionAuthGuardMiddleware> logger)
    {
        _next    = next;
        _options = options;
        _logger  = logger;
    }

    /// <summary>
    /// Processes the request, redirecting protected unauthenticated sessions to Login.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // An authenticated external-share browser is confined to the read-only
        // Share surface. It cannot reach Settings, Archive, document writes, probes,
        // or normal Dashboard routes by typing a URL directly.
        if (context.Session.GetString("ExternalShare.Authenticated") == "true" &&
            !context.Request.Path.StartsWithSegments("/Share", StringComparison.OrdinalIgnoreCase) &&
            !context.Request.Path.StartsWithSegments("/SetCulture", StringComparison.OrdinalIgnoreCase))
        {
            context.Response.Redirect("/Share/Dashboard");
            return;
        }

        // Only guard sessions that arrived via a known launch source — with one
        // exception: a direct browser session that has NO resolvable repository
        // (nothing in the session and no configured fallback) must go through
        // the login page to choose one, otherwise every API call would fail
        // with an empty repository path segment.
        var source = context.Session.GetString(SessionKeyActiveRepoSource);
        var path   = context.Request.Path;

        // Cookie authentication proves the browser identity. Repository session
        // markers remain required because the OAuth token cache is session-scoped;
        // if that session is gone the flow must reacquire a token rather than silently
        // falling back to a different credential source.
        var claimedRepoId = context.User.FindFirst(
            DashboardAuthenticationDefaults.RepositoryClaimType)?.Value;
        var hasAuthenticatedRepository =
            context.User.Identity?.IsAuthenticated == true &&
            !string.IsNullOrWhiteSpace(claimedRepoId);

        // Desktop launches always require authentication. Web Client launches use
        // the same guard when LFDS SSO is configured; with dormant/default SSO they
        // retain the legacy direct-open behavior.
        var authenticationMode = _options.CurrentValue.AuthenticationMode;
        var mustAuthenticate =
            authenticationMode == LaserficheAuthenticationMode.RepositoryPassword ||
            string.Equals(source, RepositorySessionMiddleware.SourceDesktop,
                StringComparison.OrdinalIgnoreCase) ||
            (string.Equals(source, RepositorySessionMiddleware.SourceWebClient,
                 StringComparison.OrdinalIgnoreCase) &&
             authenticationMode == LaserficheAuthenticationMode.LfdsSso &&
             _options.CurrentValue.Sso.IsConfigured) ||
            (string.IsNullOrWhiteSpace(source) &&
             authenticationMode == LaserficheAuthenticationMode.LfdsSso &&
             _options.CurrentValue.Sso.IsConfigured);

        if (!mustAuthenticate)
        {
            var sessionRepo    = context.Session.GetString(SessionKeyActiveRepoId);
            var configuredRepo = _options.CurrentValue.RepositoryId;

            if (string.IsNullOrWhiteSpace(sessionRepo) &&
                string.IsNullOrWhiteSpace(configuredRepo) &&
                !IsExcluded(path))
            {
                _logger.LogInformation(
                    "Direct browser session has no repository (none in session, none configured). " +
                    "Redirecting to /Login for repository selection.");
                context.Response.Redirect("/Login");
                return;
            }

            await _next(context);
            return;
        }

        // Never redirect paths that are part of the auth / admin / health surface.
        if (IsExcluded(path))
        {
            await _next(context);
            return;
        }

        // Require the authenticated cookie identity and bind it to the active
        // repository. The session key is retained as token/session state, but is
        // not by itself proof of an authenticated browser on subsequent requests.
        var activeRepoId        = context.Session.GetString(SessionKeyActiveRepoId);
        var authenticatedRepoId = context.Session.GetString(SessionKeyAuthenticatedRepoId);
        bool isAuthenticated =
            hasAuthenticatedRepository &&
            !string.IsNullOrWhiteSpace(authenticatedRepoId) &&
            !string.IsNullOrWhiteSpace(activeRepoId) &&
            !string.IsNullOrWhiteSpace(claimedRepoId) &&
            string.Equals(authenticatedRepoId, activeRepoId, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(claimedRepoId, activeRepoId, StringComparison.OrdinalIgnoreCase);

        if (!isAuthenticated)
        {
            _logger.LogInformation(
                "{Source} session not authenticated for repository {ActiveRepo} " +
                "(authenticated: {AuthRepo}, cookie authenticated: {CookieAuthenticated}, " +
                "claimed repository: {ClaimedRepo}). Redirecting to /Login.",
                source,
                activeRepoId ?? "(none)",
                authenticatedRepoId ?? "(none)",
                context.User.Identity?.IsAuthenticated == true,
                claimedRepoId ?? "(none)");

            var returnUrl = Uri.EscapeDataString(
                context.Request.PathBase + context.Request.Path + context.Request.QueryString);
            context.Response.Redirect($"/Login?returnUrl={returnUrl}");
            return;
        }

        await _next(context);
    }

    private static bool IsExcluded(PathString path) =>
        path.StartsWithSegments("/Login",    StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Share",    StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Launch",   StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Settings", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/health",   StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/diagnostics", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Home",     StringComparison.OrdinalIgnoreCase);
}
