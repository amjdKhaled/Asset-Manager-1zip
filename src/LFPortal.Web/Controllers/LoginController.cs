using System.ComponentModel.DataAnnotations;
using LFPortal.Application.Interfaces;
using LFPortal.Web.Middleware;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Handles the Desktop Client login flow: presents a repository-specific credential
/// form, validates credentials directly against Laserfiche, and establishes a
/// session-scoped authentication state before allowing access to the Dashboard.
/// </summary>
/// <remarks>
/// <para>
/// This controller is reached when <see cref="SessionAuthGuardMiddleware"/> detects a
/// Desktop Client session that is not yet authenticated for the active repository.
/// </para>
/// <para>
/// On successful login, <c>AuthenticatedRepositoryId</c> is written to the ASP.NET Core
/// session, and credentials are stored encrypted in <see cref="ISessionCredentialStore"/>
/// so that token-refresh requests (when the Bearer token expires) can acquire a new token
/// without re-prompting.
/// </para>
/// <para>
/// <c>GET /Login/SignOut</c> clears only the session authentication state.  It does not
/// affect Settings-stored fallback credentials.
/// </para>
/// </remarks>
public sealed class LoginController : Controller
{
    private readonly ILaserficheAuthService  _authService;
    private readonly IRepositoryContext      _repositoryContext;
    private readonly ISessionCredentialStore _sessionCredentialStore;
    private readonly ILogger<LoginController> _logger;

    /// <summary>Initialises the controller.</summary>
    public LoginController(
        ILaserficheAuthService   authService,
        IRepositoryContext       repositoryContext,
        ISessionCredentialStore  sessionCredentialStore,
        ILogger<LoginController> logger)
    {
        _authService             = authService;
        _repositoryContext       = repositoryContext;
        _sessionCredentialStore  = sessionCredentialStore;
        _logger                  = logger;
    }

    // ------------------------------------------------------------------ //
    // GET /Login                                                           //
    // ------------------------------------------------------------------ //

    /// <summary>Renders the sign-in form for the active Laserfiche repository.</summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        return View(new LoginViewModel { ActiveRepository = repo.RepositoryId });
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

        // Username is required; ModelState captures that via [Required] on LoginInputModel.
        // Password is intentionally NOT required — blank passwords are valid Laserfiche accounts.
        if (!ModelState.IsValid)
        {
            return View(new LoginViewModel
            {
                ActiveRepository   = repo.RepositoryId,
                SubmittedUsername  = input.Username
            });
        }

        // Attempt authentication — never log the password.
        bool success;
        try
        {
            success = await _authService.TryAuthenticateAsync(
                repo,
                input.Username,
                input.Password ?? string.Empty,
                cancellationToken);
        }
        catch (Exception ex)
        {
            // Infrastructure error (network failure, 5xx) — show a safe message.
            _logger.LogError(ex,
                "Login: infrastructure error while authenticating for repository {RepoId}.",
                repo.RepositoryId);

            return View(new LoginViewModel
            {
                ActiveRepository  = repo.RepositoryId,
                SubmittedUsername = input.Username,
                ErrorMessage      =
                    "Could not reach the Laserfiche server. " +
                    "Check your network connection and try again."
            });
        }

        if (!success)
        {
            _logger.LogInformation(
                "Login: invalid credentials submitted for repository {RepoId}.",
                repo.RepositoryId);

            return View(new LoginViewModel
            {
                ActiveRepository  = repo.RepositoryId,
                SubmittedUsername = input.Username,
                ErrorMessage      =
                    $"Unable to sign in to {repo.RepositoryId}. " +
                    "Check the username and password."
            });
        }

        // ── Authentication succeeded ──────────────────────────────────────────

        // Store encrypted credentials in session so the token service can refresh
        // the Bearer token when it expires without re-prompting the user.
        await _sessionCredentialStore.StoreAsync(
            input.Username,
            input.Password ?? string.Empty,
            cancellationToken);

        // Mark this session as authenticated for the active repository.
        HttpContext.Session.SetString(
            SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId,
            repo.RepositoryId);

        _logger.LogInformation(
            "Login: session authenticated for repository {RepoId}.",
            repo.RepositoryId);

        return RedirectToAction("Index", "Dashboard");
    }

    // ------------------------------------------------------------------ //
    // GET /Login/SignOut                                                   //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Clears the session authentication state and session credentials, then
    /// redirects to the Login page.  The active repository is preserved.
    /// Settings-stored fallback credentials are not affected.
    /// </summary>
    [HttpGet("/Login/SignOut")]
    public async Task<IActionResult> SignOut(CancellationToken cancellationToken)
    {
        HttpContext.Session.Remove(SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId);
        await _sessionCredentialStore.ClearAsync(cancellationToken);

        _logger.LogInformation("Login: session authentication cleared (Change Account).");

        return RedirectToAction("Index", "Login");
    }
}

// ── View models ──────────────────────────────────────────────────────────────

/// <summary>View model passed to the Login view.</summary>
public sealed class LoginViewModel
{
    /// <summary>Repository name displayed read-only on the login form.</summary>
    public string ActiveRepository { get; init; } = string.Empty;

    /// <summary>Error message shown below the form after a failed sign-in attempt. Null when none.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Preserves the username the user entered so the field is re-populated
    /// when the form is returned after a failed sign-in attempt.
    /// </summary>
    public string SubmittedUsername { get; init; } = string.Empty;
}

/// <summary>
/// Binds the login form POST payload.
/// Password is intentionally <em>not</em> marked <c>[Required]</c> — some Laserfiche
/// accounts have an empty password and must be accepted without a validation error.
/// </summary>
public sealed class LoginInputModel
{
    /// <summary>Laserfiche username. Required.</summary>
    [Required(ErrorMessage = "Username is required.")]
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Laserfiche password. Optional — a <c>null</c> or empty value is treated as
    /// an intentional blank password and sent to Laserfiche as-is.
    /// Do NOT add <c>[Required]</c> here.
    /// </summary>
    public string? Password { get; set; }
}
