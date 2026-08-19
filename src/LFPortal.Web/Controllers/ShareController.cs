using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Options;
using LFPortal.Web.Authentication;
using LFPortal.Web.Middleware;
using LFPortal.Web.Options;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Provides an isolated, Repository Password-only gateway for temporary external
/// Dashboard access. These routes never enter the LFDS/OAuth flow.
/// </summary>
[Route("Share")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class ShareController : Controller
{
    internal const string SessionKeyAccessGranted = "ExternalShare.AccessGranted";
    internal const string SessionKeyAuthenticated = "ExternalShare.Authenticated";
    internal const string SessionKeyUsername = "ExternalShare.Username";
    private const string SessionKeyExpiresUtc = "ExternalShare.ExpiresUtc";

    private readonly ILaserficheAuthService _authService;
    private readonly ILaserficheDashboardService _dashboardService;
    private readonly IRepositoryContext _repositoryContext;
    private readonly IOptionsMonitor<ExternalShareOptions> _shareOptions;
    private readonly IOptionsMonitor<LaserficheOptions> _laserficheOptions;
    private readonly ILogger<ShareController> _logger;

    public ShareController(
        ILaserficheAuthService authService,
        ILaserficheDashboardService dashboardService,
        IRepositoryContext repositoryContext,
        IOptionsMonitor<ExternalShareOptions> shareOptions,
        IOptionsMonitor<LaserficheOptions> laserficheOptions,
        ILogger<ShareController> logger)
    {
        _authService = authService;
        _dashboardService = dashboardService;
        _repositoryContext = repositoryContext;
        _shareOptions = shareOptions;
        _laserficheOptions = laserficheOptions;
        _logger = logger;
    }

    [HttpGet("/Share/Login")]
    [AllowAnonymous]
    public IActionResult Login(string? key = null)
    {
        var options = _shareOptions.CurrentValue;
        if (!options.Enabled)
            return NotFound();

        var alreadyGranted = HasUnexpiredAccessGrant();
        if (!alreadyGranted && !AccessKeysMatch(key, options.AccessKey))
            return StatusCode(StatusCodes.Status403Forbidden);

        HttpContext.Session.SetString(SessionKeyAccessGranted, "true");
        HttpContext.Session.SetString(
            SessionKeyExpiresUtc,
            DateTimeOffset.UtcNow.AddHours(2).ToUnixTimeSeconds().ToString());
        return View(BuildLoginViewModel());
    }

    [HttpPost("/Share/Login")]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(
        ExternalShareLoginInput input,
        CancellationToken cancellationToken)
    {
        var options = _shareOptions.CurrentValue;
        if (!options.Enabled)
            return NotFound();

        if (!HasUnexpiredAccessGrant())
            return StatusCode(StatusCodes.Status403Forbidden);

        var allowedRepositories = GetAllowedRepositories();
        if (!allowedRepositories.Contains(input.Repository ?? string.Empty, StringComparer.OrdinalIgnoreCase))
            ModelState.AddModelError(nameof(input.Repository), "Select a configured repository.");

        if (!ModelState.IsValid)
            return View(BuildLoginViewModelWithoutPassword(input));

        var activeRepository = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        var repositoryId = input.Repository!.Trim();
        var targetRepository = activeRepository with
        {
            RepositoryId = repositoryId,
            DisplayName = repositoryId
        };

        bool authenticated;
        try
        {
            // This is intentionally Repository Password authentication, not LFDS SSO.
            authenticated = await _authService.TryAuthenticateAsync(
                targetRepository,
                input.Username,
                input.Password ?? string.Empty,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "External Share login could not reach repository {RepositoryId}.", repositoryId);
            return View(BuildLoginViewModelWithoutPassword(
                input,
                "The repository is unavailable. Try again later."));
        }

        if (!authenticated)
        {
            _logger.LogInformation(
                "External Share rejected credentials for repository {RepositoryId}.", repositoryId);
            return View(BuildLoginViewModelWithoutPassword(
                input,
                "The repository username or password is incorrect."));
        }

        HttpContext.Session.SetString(RepositorySessionMiddleware.SessionKeyRepositoryId, repositoryId);
        HttpContext.Session.SetString(RepositorySessionMiddleware.SessionKeySource, "External Share");
        HttpContext.Session.SetString(SessionAuthGuardMiddleware.SessionKeyAuthenticatedRepoId, repositoryId);
        HttpContext.Session.SetString(SessionKeyAuthenticated, "true");
        HttpContext.Session.SetString(SessionKeyUsername, input.Username);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.Name, input.Username),
                new Claim(ExternalShareAuthenticationDefaults.RepositoryClaim, repositoryId),
                new Claim(
                    ExternalShareAuthenticationDefaults.AuthenticationMethodClaim,
                    ExternalShareAuthenticationDefaults.AuthenticationMethod)
            ],
            ExternalShareAuthenticationDefaults.Scheme);

        await HttpContext.SignInAsync(
            ExternalShareAuthenticationDefaults.Scheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = false,
                AllowRefresh = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(2)
            });

        _logger.LogInformation(
            "External Share session established for repository {RepositoryId}.", repositoryId);

        return Redirect("/Share/Dashboard");
    }

    [HttpGet("/Share/Dashboard")]
    [Authorize(AuthenticationSchemes = ExternalShareAuthenticationDefaults.Scheme)]
    public async Task<IActionResult> Dashboard(CancellationToken cancellationToken)
    {
        if (!_shareOptions.CurrentValue.Enabled ||
            HttpContext.Session.GetString(SessionKeyAuthenticated) != "true")
        {
            await ClearShareSessionAsync(cancellationToken);
            return Redirect("/Share/Login");
        }

        var claimRepository = User.FindFirstValue(ExternalShareAuthenticationDefaults.RepositoryClaim);
        var sessionRepository = HttpContext.Session.GetString(RepositorySessionMiddleware.SessionKeyRepositoryId);
        if (string.IsNullOrWhiteSpace(claimRepository) ||
            !string.Equals(claimRepository, sessionRepository, StringComparison.OrdinalIgnoreCase))
        {
            await ClearShareSessionAsync(cancellationToken);
            return Redirect("/Share/Login");
        }

        var stats = await _dashboardService.GetDashboardStatsAsync(cancellationToken);
        // External Share is always rendered read-only. The option is retained as
        // an explicit deployment declaration, not as a switch that enables writes.
        ViewData["ExternalShareReadOnly"] = true;
        ViewData["ExternalShareUsername"] = User.Identity?.Name ?? string.Empty;
        return View("~/Views/Dashboard/Index.cshtml", new DashboardViewModel { Stats = stats });
    }

    private ExternalShareLoginViewModel BuildLoginViewModel(
        ExternalShareLoginInput? input = null,
        string? error = null) => new()
    {
        Input = input ?? new ExternalShareLoginInput(),
        Repositories = GetAllowedRepositories(),
        ErrorMessage = error
    };

    private ExternalShareLoginViewModel BuildLoginViewModelWithoutPassword(
        ExternalShareLoginInput input,
        string? error = null)
    {
        ModelState.Remove(nameof(ExternalShareLoginInput.Password));
        return BuildLoginViewModel(new ExternalShareLoginInput
        {
            Repository = input.Repository,
            Username = input.Username,
            Password = null
        }, error);
    }

    private IReadOnlyList<string> GetAllowedRepositories()
    {
        var configured = _shareOptions.CurrentValue.Repositories
            .Where(repository => !string.IsNullOrWhiteSpace(repository))
            .Select(repository => repository.Trim())
            .ToList();

        var fallback = _laserficheOptions.CurrentValue.RepositoryId?.Trim();
        if (configured.Count == 0 && !string.IsNullOrWhiteSpace(fallback))
            configured.Add(fallback);

        return configured.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private async Task ClearShareSessionAsync(CancellationToken cancellationToken)
    {
        await HttpContext.SignOutAsync(ExternalShareAuthenticationDefaults.Scheme);
        await _authService.InvalidateCurrentSessionTokensAsync();
        HttpContext.Session.Clear();
    }

    private bool HasUnexpiredAccessGrant() =>
        HttpContext.Session.GetString(SessionKeyAccessGranted) == "true" &&
        long.TryParse(HttpContext.Session.GetString(SessionKeyExpiresUtc), out var expiresAt) &&
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() < expiresAt;

    private static bool AccessKeysMatch(string? supplied, string configured)
    {
        if (string.IsNullOrEmpty(supplied) || string.IsNullOrEmpty(configured))
            return false;

        var suppliedHash = SHA256.HashData(Encoding.UTF8.GetBytes(supplied));
        var configuredHash = SHA256.HashData(Encoding.UTF8.GetBytes(configured));
        return CryptographicOperations.FixedTimeEquals(suppliedHash, configuredHash);
    }
}

public sealed class ExternalShareLoginInput
{
    [Required]
    public string? Repository { get; set; }

    [Required]
    public string Username { get; set; } = string.Empty;

    [Required, DataType(DataType.Password)]
    public string? Password { get; set; }
}

public sealed class ExternalShareLoginViewModel
{
    public ExternalShareLoginInput Input { get; init; } = new();
    public IReadOnlyList<string> Repositories { get; init; } = [];
    public string? ErrorMessage { get; init; }
}
