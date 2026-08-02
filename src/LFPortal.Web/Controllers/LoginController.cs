using System.ComponentModel.DataAnnotations;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Exceptions;
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
        return View(new LoginViewModel
        {
            ActiveRepository     = repo.RepositoryId,
            AllowRepositoryInput = AllowRepositoryInput(),
            SubmittedRepository  = repo.RepositoryId
        });
    }

    /// <summary>
    /// Repository selection is allowed only for direct browser sessions.
    /// Sessions launched from the Laserfiche Desktop or Web Client carry their
    /// repository in the launch URL and must not be able to switch it here.
    /// </summary>
    private bool AllowRepositoryInput()
    {
        var source = HttpContext.Session.GetString(
            RepositorySessionMiddleware.SessionKeySource);
        return string.IsNullOrEmpty(source);
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

        // Resolve which repository this login targets:
        //   - Direct browser sessions may type/override the repository name.
        //   - Desktop / Web Client sessions always use the launch-context repository.
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

        // Username is required; ModelState captures that via [Required] on LoginInputModel.
        // Password is intentionally NOT required — blank passwords are valid Laserfiche accounts.
        if (!ModelState.IsValid)
        {
            return View(ViewWithError(null));
        }

        if (string.IsNullOrWhiteSpace(repoId))
        {
            return View(ViewWithError(
                "Enter the name of the Laserfiche repository to sign in to."));
        }

        // Repository is runtime session context: authenticate against exactly
        // the repository this session targets.
        var targetRepo = repo with { RepositoryId = repoId, DisplayName = repoId };

        // Log the attempt before hitting the network — gives administrators a
        // clear entry point in the log for diagnosing login failures.
        // NEVER log the password.
        _logger.LogInformation(
            "Login: authenticating user {Username} for repository {RepoId}.",
            input.Username, repoId);

        // Attempt authentication — never log the password.
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
                "Login: error while authenticating for repository {RepoId}: {ErrorType}.",
                repoId, ex.GetType().Name);

            return View(ViewWithError(ClassifyLoginError(ex, repoId)));
        }

        if (!success)
        {
            _logger.LogInformation(
                "Login: invalid credentials submitted for repository {RepoId}.",
                repoId);

            return View(ViewWithError(
                $"Unable to sign in to {repoId}. Check the username and password."));
        }

        // ── Authentication succeeded ──────────────────────────────────────────

        // Store encrypted credentials in session so the token service can refresh
        // the Bearer token when it expires without re-prompting the user.
        await _sessionCredentialStore.StoreAsync(
            input.Username,
            input.Password ?? string.Empty,
            cancellationToken);

        // Record the repository this session now works against (important for
        // direct browser sessions that selected a repository on this form) and
        // mark the session as authenticated for it.
        HttpContext.Session.SetString(
            RepositorySessionMiddleware.SessionKeyRepositoryId,
            repoId);
        HttpContext.Session.SetString(
            SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId,
            repoId);

        _logger.LogInformation(
            "Login: session authenticated for repository {RepoId}.",
            repoId);

        return RedirectToAction("Index", "Dashboard");
    }

    // ------------------------------------------------------------------ //
    // Error classification                                                 //
    // ------------------------------------------------------------------ //

    /// <summary>
    /// Maps an authentication failure to a precise, user-facing message that
    /// distinguishes TLS problems, connectivity failures, timeouts, an unknown
    /// repository, and Laserfiche server errors — instead of a single generic
    /// "could not reach the server" message.
    /// </summary>
    private static string ClassifyLoginError(Exception ex, string repoId)
    {
        // Laserfiche API answered with an HTTP error the auth service propagated.
        if (ex is LaserficheException lex)
        {
            if (lex.StatusCode == 404)
                return $"Repository \"{repoId}\" was not found on the Laserfiche server. " +
                       "Check the repository name (it is case-sensitive).";

            if (lex.StatusCode >= 500)
                return $"The Laserfiche server reported an internal error (HTTP {lex.StatusCode}). " +
                       "Try again, or contact your Laserfiche administrator.";

            return $"The Laserfiche server rejected the request (HTTP {lex.StatusCode}).";
        }

        // Transport-level causes — shared classifier evaluates the precise
        // HttpRequestError kind first (DNS / TLS / refused / timeout) before
        // falling back to inner-exception inspection.
        return Diagnostics.LaserficheErrorClassifier.Classify(ex).Detail;
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

    /// <summary>
    /// True for direct browser sessions, which may choose the repository on the
    /// login form. False for Desktop / Web Client launches, where the repository
    /// comes from the launch context and is displayed read-only.
    /// </summary>
    public bool AllowRepositoryInput { get; init; }

    /// <summary>Preserves the repository name the user entered after a failed attempt.</summary>
    public string SubmittedRepository { get; init; } = string.Empty;

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
    /// <summary>
    /// Repository name for direct browser sessions. Ignored for Desktop /
    /// Web Client launches, whose repository is fixed by the launch context.
    /// </summary>
    public string? Repository { get; set; }

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
