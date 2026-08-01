namespace LFPortal.Web.Middleware;

/// <summary>
/// Intercepts incoming HTTP requests to capture the active Laserfiche repository
/// when the portal is opened via the Dashboard Desktop Client toolbar button.
/// </summary>
/// <remarks>
/// <para>
/// The Desktop Client button command appends <c>?repository=&lt;DatabaseName&gt;</c>
/// to the portal URL. This middleware reads that parameter, validates it, and stores
/// it in the ASP.NET Core session so that <c>SessionAwareRepositoryContext</c>
/// can serve it on every subsequent request in the same browser session.
/// </para>
/// <para>
/// A companion session key (<c>ActiveRepositorySource</c>) is set to
/// <c>"Laserfiche Desktop Client"</c> so that the Settings page can show users
/// where the active repository is coming from.
/// </para>
/// <para>
/// If the parameter is absent the session is left unchanged, preserving any
/// previously stored value or falling back to the configured default.
/// </para>
/// </remarks>
public sealed class RepositorySessionMiddleware
{
    private const string QueryParam             = "repository";
    private const string SessionKeyRepositoryId = "ActiveRepositoryId";
    private const string SessionKeySource       = "ActiveRepositorySource";

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

    /// <summary>Processes the request, capturing any <c>?repository=</c> parameter.</summary>
    public async Task InvokeAsync(HttpContext context)
    {
        var repoParam = context.Request.Query[QueryParam].FirstOrDefault();

        if (!string.IsNullOrWhiteSpace(repoParam) && IsValidRepositoryId(repoParam))
        {
            var trimmed = repoParam.Trim();

            context.Session.SetString(SessionKeyRepositoryId, trimmed);
            context.Session.SetString(SessionKeySource, "Laserfiche Desktop Client");

            _logger.LogInformation(
                "Active repository set from Laserfiche Desktop Client: {RepositoryId}", trimmed);
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
