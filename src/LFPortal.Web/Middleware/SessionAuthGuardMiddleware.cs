namespace LFPortal.Web.Middleware;

/// <summary>
/// Guards protected routes for Desktop Client sessions that have not yet
/// completed the Login flow for the currently active repository.
/// </summary>
/// <remarks>
/// <para>
/// The guard fires only when the session was opened from the Laserfiche Desktop Client
/// (i.e. <c>ActiveRepositorySource == "Laserfiche Desktop Client"</c>).  For direct
/// browser access the guard is transparent — the existing Settings-configured fallback
/// credentials are used without any login prompt.
/// </para>
/// <para>
/// A session is considered authenticated when <c>AuthenticatedRepositoryId</c> matches
/// <c>ActiveRepositoryId</c>.  If the user switches repositories in the Desktop Client,
/// a new popup is opened, <c>ActiveRepositoryId</c> changes, and the guard redirects to
/// <c>/Login</c> again because the old authentication is for a different repository.
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
    private  const string DesktopClientSource           = "Laserfiche Desktop Client";

    private readonly RequestDelegate _next;
    private readonly ILogger<SessionAuthGuardMiddleware> _logger;

    /// <summary>Initialises the middleware.</summary>
    public SessionAuthGuardMiddleware(
        RequestDelegate next,
        ILogger<SessionAuthGuardMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    /// <summary>Processes the request, redirecting unauthenticated Desktop Client sessions to Login.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        // Only guard Desktop Client sessions.
        var source = context.Session.GetString(SessionKeyActiveRepoSource);
        if (source != DesktopClientSource)
        {
            await _next(context);
            return;
        }

        // Never redirect paths that are part of the auth / admin / health surface.
        var path = context.Request.Path;
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
                "Desktop Client session not authenticated for repository {ActiveRepo} " +
                "(authenticated: {AuthRepo}). Redirecting to /Login.",
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
        path.StartsWithSegments("/Home",     StringComparison.OrdinalIgnoreCase);
}
