namespace LFPortal.Web.Middleware;

/// <summary>
/// Intercepts incoming HTTP requests to capture the active Laserfiche repository
/// when the portal is opened via the Dashboard toolbar button — either from the
/// Desktop Client or from the Laserfiche Web Client.
/// </summary>
/// <remarks>
/// <para>
/// <b>Desktop Client:</b> The Dashboard Desktop Extension appends
/// <c>?repository=&lt;DatabaseName&gt;</c> to the portal URL (no <c>source</c> parameter).
/// This middleware records the source as <c>"Laserfiche Desktop Client"</c>.
/// </para>
/// <para>
/// <b>Web Client:</b> The Web Client button script appends
/// <c>?repository=&lt;repo&gt;&amp;source=webclient</c>.
/// This middleware records the source as <c>"Laserfiche Web Client"</c>.
/// </para>
/// <para>
/// A companion session key (<c>ActiveRepositorySource</c>) carries the validated
/// source label so the Settings page, header badge, and auth guard can all branch
/// on launch context without re-parsing the URL.
/// </para>
/// <para>
/// If <c>?repository=</c> is absent the session is left unchanged, preserving any
/// previously stored value or falling back to the configured default.
/// </para>
/// </remarks>
public sealed class RepositorySessionMiddleware
{
    internal const string QueryParamRepository  = "repository";
    internal const string QueryParamSource      = "source";
    internal const string SessionKeyRepositoryId = "ActiveRepositoryId";
    internal const string SessionKeySource       = "ActiveRepositorySource";

    internal const string SourceDesktop   = "Laserfiche Desktop Client";
    internal const string SourceWebClient = "Laserfiche Web Client";

    private readonly RequestDelegate _next;
    private readonly ILogger<RepositorySessionMiddleware> _logger;

    /// <summary>Initialises the middleware with the next delegate and a logger.</summary>
    public RepositorySessionMiddleware(
        RequestDelegate next,
        ILogger<RepositorySessionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes the request, capturing any <c>?repository=</c> and <c>?source=</c>
    /// parameters and writing validated values into the ASP.NET Core session.
    /// </summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var repoParam = context.Request.Query[QueryParamRepository].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(repoParam) && IsValidRepositoryId(repoParam))
        {
            var trimmed = repoParam.Trim();

            // Determine the launch source from the optional ?source= parameter.
            // "webclient" → Laserfiche Web Client; anything else (including absent) →
            // Laserfiche Desktop Client for full backward-compatibility with the existing
            // Desktop Extension which does not send a source parameter.
            var sourceParam = context.Request.Query[QueryParamSource].FirstOrDefault() ?? string.Empty;
            var source      = sourceParam.Equals("webclient", StringComparison.OrdinalIgnoreCase)
                ? SourceWebClient
                : SourceDesktop;

            // The incoming ?repository= always overrides a previously stored value so
            // that repository-switching (e.g. the user opens a new popup for a different
            // repository) takes effect immediately.
            context.Session.SetString(SessionKeyRepositoryId, trimmed);
            context.Session.SetString(SessionKeySource,       source);

            _logger.LogInformation(
                "Active repository set from {Source}: {RepositoryId}", source, trimmed);
        }

        await _next(context);
    }

    /// <summary>
    /// Lightweight sanity check on the repository identifier from the URL.
    /// Rejects values that are suspiciously long or contain control characters.
    /// The Laserfiche API itself will reject any repository that doesn't exist,
    /// so this guard is purely defensive against malformed inputs.
    /// </summary>
    private static bool IsValidRepositoryId(string value)
    {
        if (value.Length > 200)
            return false;

        foreach (var c in value)
        {
            if (char.IsControl(c))
                return false;
        }

        return true;
    }
}
