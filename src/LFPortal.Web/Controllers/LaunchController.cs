using LFPortal.Application.Interfaces;
using LFPortal.Web.Authentication;
using LFPortal.Web.Middleware;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Creates a clean Dashboard-owned authentication boundary for a Web Client launch,
/// then presents a brief transition page before starting the supported LFDS flow.
/// </summary>
public sealed class LaunchController : Controller
{
    private readonly ILaserficheAuthService _authService;
    private readonly ISessionCredentialStore _sessionCredentialStore;
    private readonly IOAuthTransactionCookie _oAuthTransactionCookie;
    private readonly ILogger<LaunchController> _logger;

    public LaunchController(
        ILaserficheAuthService authService,
        ISessionCredentialStore sessionCredentialStore,
        IOAuthTransactionCookie oAuthTransactionCookie,
        ILogger<LaunchController> logger)
    {
        _authService = authService;
        _sessionCredentialStore = sessionCredentialStore;
        _oAuthTransactionCookie = oAuthTransactionCookie;
        _logger = logger;
    }

    /// <summary>
    /// Clears only state owned by Dashboard and renders the LFDS transition page.
    /// Neither Laserfiche Web Client cookies nor LFDS cookies are read or modified.
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
        var oldUser = User.Identity?.Name ??
            HttpContext.Session.GetString(LoginController.SessionKeyAuthenticatedUser) ?? "(unknown)";

        // Invalidate before removing scope keys: token invalidation needs the old
        // Dashboard session identity to locate the correct per-user cache generation.
        await _authService.InvalidateCurrentSessionTokensAsync();
        await HttpContext.SignOutAsync(DashboardAuthenticationDefaults.Scheme);
        _oAuthTransactionCookie.Delete(HttpContext);
        await _sessionCredentialStore.ClearAsync(cancellationToken);

        RemoveDashboardSessionState(HttpContext.Session);
        HttpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity());

        var redirectUrl = Url.Action(
            "StartSso",
            "Login",
            new { repository = repositoryId, returnUrl = safeReturnUrl });
        if (string.IsNullOrWhiteSpace(redirectUrl) || !Url.IsLocalUrl(redirectUrl))
            throw new InvalidOperationException("Could not generate a safe local LFDS start URL.");

        _logger.LogInformation(
            "Dashboard launch state cleared. Repository={RepositoryId}; OldUser={OldUser}; " +
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
