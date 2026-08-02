using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Middleware;

/// <summary>
/// Guards protected routes for Desktop Client or Web Client sessions that have not yet
/// completed the Login flow for the currently active repository.
/// </summary>
/// <remarks>
/// <para>
/// The guard fires when the session was opened from either the Laserfiche Desktop Client
/// (<c>ActiveRepositorySource == "Laserfiche Desktop Client"</c>) or the Laserfiche
/// Web Client (<c>ActiveRepositorySource == "Laserfiche Web Client"</c>).
/// For direct browser access the guard is transparent — the existing Settings-configured
/// fallback credentials are used without any login prompt.
/// </para>
/// <para>
/// A session is considered authenticated when <c>AuthenticatedRepositoryId</c> matches
/// <c>ActiveRepositoryId</c>.  Switching repositories (e.g. the user opens a new popup or
/// tab for a different repository) causes <c>ActiveRepositoryId</c> to change and the
/// guard redirects to <c>/Login</c> again because the prior authentication is for a
/// different repository.
/// </para>
/// <para>
/// Excluded paths (never redirected):
/// <list type="bullet">
///   <item><c>/Login</c> and all sub-paths</item>
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
    /// Sources that require an explicit Login before accessing repository data.
    /// Direct browser access (null / "Default Configuration") is not in this set
    /// and falls through to the Settings-page fallback credentials.
    /// </summary>
    private static readonly HashSet<string> GuardedSources =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Laserfiche Desktop Client",
            "Laserfiche Web Client",
        };

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
    /// Processes the request, redirecting unauthenticated Desktop Client or
    /// Web Client sessions to Login.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Only guard sessions that arrived via a known launch source — with one
        // exception: a direct browser session that has NO resolvable repository
        // (nothing in the session and no configured fallback) must go through
        // the login page to choose one, otherwise every API call would fail
        // with an empty repository path segment.
        var source = context.Session.GetString(SessionKeyActiveRepoSource);
        var path   = context.Request.Path;

        if (!GuardedSources.Contains(source ?? string.Empty))
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

        // Check whether the session is authenticated for the currently active repository.
        var activeRepoId        = context.Session.GetString(SessionKeyActiveRepoId);
        var authenticatedRepoId = context.Session.GetString(SessionKeyAuthenticatedRepoId);

        bool isAuthenticated =
            !string.IsNullOrWhiteSpace(authenticatedRepoId) &&
            !string.IsNullOrWhiteSpace(activeRepoId) &&
            string.Equals(authenticatedRepoId, activeRepoId, StringComparison.OrdinalIgnoreCase);

        if (!isAuthenticated)
        {
            _logger.LogInformation(
                "{Source} session not authenticated for repository {ActiveRepo} " +
                "(authenticated: {AuthRepo}). Redirecting to /Login.",
                source,
                activeRepoId ?? "(none)",
                authenticatedRepoId ?? "(none)");

            context.Response.Redirect("/Login");
            return;
        }

        await _next(context);
    }

    private static bool IsExcluded(PathString path) =>
        path.StartsWithSegments("/Login",    StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Settings", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/health",   StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/diagnostics", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/Home",     StringComparison.OrdinalIgnoreCase);
}
