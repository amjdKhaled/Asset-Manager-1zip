using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Options;
using LFPortal.Web.Authentication;
using LFPortal.Web.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Handles Dashboard launches coming from the Laserfiche Web Client.
/// Repository-password mode preserves the Dashboard authentication flow, while
/// LFDS mode creates a clean authentication boundary before starting SSO.
/// </summary>
public sealed class LaunchController : Controller
{
    private readonly ILaserficheAuthService _authService;
    private readonly ISessionCredentialStore _sessionCredentialStore;
    private readonly IOAuthTransactionCookie _oAuthTransactionCookie;
    private readonly IOptionsMonitor<LaserficheOptions> _options;
    private readonly ILogger<LaunchController> _logger;

    public LaunchController(
        ILaserficheAuthService authService,
        ISessionCredentialStore sessionCredentialStore,
        IOAuthTransactionCookie oAuthTransactionCookie,
        IOptionsMonitor<LaserficheOptions> options,
        ILogger<LaunchController> logger)
    {
        _authService = authService;
        _sessionCredentialStore = sessionCredentialStore;
        _oAuthTransactionCookie = oAuthTransactionCookie;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// Accepts a validated Web Client launch. In RepositoryPassword mode the route
    /// only records the repository/source and continues to the requested Dashboard
    /// page; the auth guard will show /Login once if authentication is still needed.
    /// In LFDS SSO mode it clears Dashboard-owned auth state and starts the supported
    /// LFDS transition flow.
    /// </summary>
    [HttpGet("/Launch")]
    [ResponseCache(Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Index(
        string? repository,
        string? source,
        string? returnUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(source, "webclient", StringComparison.OrdinalIgnoreCase) ||
            !IsValidRepositoryId(repository))
        {
            _logger.LogWarning(
                "Rejected Dashboard launch with invalid source or repository. Source={Source}.",
                source ?? "(none)");
            return BadRequest("A valid Web Client repository launch is required.");
        }

        var repositoryId = repository!.Trim();
        var safeReturnUrl = Url.IsLocalUrl(returnUrl) ? returnUrl! : "/Dashboard";

        // RepositoryPassword already owns its authentication through /Login. Do not
        // clear the cookie/session here and do not send the browser through StartSso.
        // If the browser is not authenticated (or is authenticated for another repo),
        // SessionAuthGuardMiddleware will redirect to /Login exactly once on the next
        // protected request. After a successful POST /Login, returning through /Launch
        // therefore continues directly to the Dashboard instead of asking for login again.
        if (_options.CurrentValue.AuthenticationMode ==
            LaserficheAuthenticationMode.RepositoryPassword)
        {
            HttpContext.Session.SetString(
                RepositorySessionMiddleware.SessionKeyRepositoryId,
                repositoryId);
            HttpContext.Session.SetString(
                RepositorySessionMiddleware.SessionKeySource,
                RepositorySessionMiddleware.SourceWebClient);

            _logger.LogInformation(
                "Web Client launch using RepositoryPassword. Repository={RepositoryId}; " +
                "preserving Dashboard auth state and redirecting to {RedirectTarget}.",
                repositoryId,
                safeReturnUrl);

            return LocalRedirect(safeReturnUrl);
        }

        var oldUser = User.Identity?.Name ??
            HttpContext.Session.GetString(LoginController.SessionKeyAuthenticatedUser) ?? "(unknown)";

        // LFDS mode: invalidate before removing scope keys because token invalidation
        // needs the old Dashboard session identity to locate the correct cache generation.
        await _authService.InvalidateCurrentSessionTokensAsync();
        await HttpContext.SignOutAsync(DashboardAuthenticationDefaults.Scheme);
        _oAuthTransactionCookie.Delete(HttpContext);
        await _sessionCredentialStore.ClearAsync(cancellationToken);

        RemoveDashboardSessionState(HttpContext.Session);

        // /Launch is only accepted for a validated Web Client launch. Restore the
        // repository/source immediately after clearing old Dashboard state so the
        // header badge keeps showing WEB CLIENT throughout the LFDS authentication flow.
        HttpContext.Session.SetString(RepositorySessionMiddleware.SessionKeyRepositoryId, repositoryId);
        HttpContext.Session.SetString(RepositorySessionMiddleware.SessionKeySource, RepositorySessionMiddleware.SourceWebClient);

        HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity());

        var redirectUrl = Url.Action(
            "StartSso",
            "Login",
            new { repository = repositoryId, returnUrl = safeReturnUrl });
        if (string.IsNullOrWhiteSpace(redirectUrl) || !Url.IsLocalUrl(redirectUrl))
            throw new InvalidOperationException("Could not generate a safe local LFDS start URL.");

        _logger.LogInformation(
            "Dashboard launch state cleared and Web Client source preserved. Repository={RepositoryId}; OldUser={OldUser}; " +
            "RedirectTarget={RedirectTarget}; ForceLogin=false.",
            repositoryId,
            oldUser,
            redirectUrl);

        return View("LaunchLoading", new LaunchLoadingViewModel
        {
            RepositoryId = repositoryId,
            RedirectUrl = redirectUrl,
        });
    }

    private static void RemoveDashboardSessionState(ISession session)
    {
        session.Remove(RepositorySessionMiddleware.SessionKeyRepositoryId);
        session.Remove(RepositorySessionMiddleware.SessionKeySource);
        session.Remove(SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId);
        session.Remove(LoginController.SessionKeyAuthenticatedUser);
        session.Remove(LoginController.SessionKeyOAuthPendingState);
        session.Remove("AuthenticationScopeMethod");
        session.Remove("AuthenticationScopeSubject");
    }

    private static bool IsValidRepositoryId(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 200)
            return false;

        return !value.Any(char.IsControl);
    }
}

public sealed class LaunchLoadingViewModel
{
    public required string RepositoryId { get; init; }
    public required string RedirectUrl { get; init; }
}
