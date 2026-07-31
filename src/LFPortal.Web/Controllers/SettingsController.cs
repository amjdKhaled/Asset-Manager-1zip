using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Provides the Settings page for configuring the Laserfiche connection, storing
/// credentials, and testing the connection before saving. All connection logic
/// is delegated to Application-layer services — no HTTP calls in this controller.
/// </summary>
public sealed class SettingsController : Controller
{
    private const string DefaultRepositoryKey = "default";

    private readonly ILaserficheRepositoryService _repositoryService;
    private readonly ICredentialProvider _credentialProvider;
    private readonly IPortalConfigurationService _portalConfig;
    private readonly ILaserficheAuthService _authService;
    private readonly IRepositoryContext _repositoryContext;
    private readonly IOptionsMonitor<LaserficheOptions> _optionsMonitor;
    private readonly ILogger<SettingsController> _logger;

    /// <summary>Initialises the controller with required services.</summary>
    public SettingsController(
        ILaserficheRepositoryService repositoryService,
        ICredentialProvider credentialProvider,
        IPortalConfigurationService portalConfig,
        ILaserficheAuthService authService,
        IRepositoryContext repositoryContext,
        IOptionsMonitor<LaserficheOptions> optionsMonitor,
        ILogger<SettingsController> logger)
    {
        _repositoryService  = repositoryService;
        _credentialProvider = credentialProvider;
        _portalConfig       = portalConfig;
        _authService        = authService;
        _repositoryContext  = repositoryContext;
        _optionsMonitor     = optionsMonitor;
        _logger             = logger;
    }

    /// <summary>Displays the Settings page pre-populated with the current configuration.</summary>
    [HttpGet]
    public IActionResult Index(bool saved = false)
    {
        return View(BuildViewModel(saved: saved));
    }

    /// <summary>
    /// Saves connection settings and (optionally) credentials, then invalidates
    /// the token cache so new values take effect immediately.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Save(
        [FromForm] SaveSettingsRequest request,
        CancellationToken cancellationToken)
    {
        // Basic validation
        if (string.IsNullOrWhiteSpace(request.ServerUrl))
            ModelState.AddModelError(nameof(request.ServerUrl), "Server URL is required.");
        if (string.IsNullOrWhiteSpace(request.RepositoryId))
            ModelState.AddModelError(nameof(request.RepositoryId), "Repository ID is required.");
        if (!string.IsNullOrWhiteSpace(request.Username) && string.IsNullOrWhiteSpace(request.Password))
            ModelState.AddModelError(nameof(request.Password), "Password is required when a username is provided.");

        if (!ModelState.IsValid)
        {
            var errorMessages = ModelState.Values
                .SelectMany(v => v.Errors)
                .Select(e => e.ErrorMessage);
            return View("Index", BuildViewModel(error: string.Join(" ", errorMessages)));
        }

        try
        {
            // 1. Persist connection settings (ServerUrl, RepositoryId, DisplayName)
            await _portalConfig.SaveConnectionSettingsAsync(
                request.ServerUrl.Trim(),
                request.RepositoryId.Trim(),
                request.DisplayName?.Trim() ?? string.Empty,
                cancellationToken);

            // 2. Persist credentials only when both fields are supplied
            if (!string.IsNullOrWhiteSpace(request.Username) &&
                !string.IsNullOrWhiteSpace(request.Password))
            {
                await _credentialProvider.StoreCredentialsAsync(
                    DefaultRepositoryKey,
                    request.Username.Trim(),
                    request.Password,
                    cancellationToken);
            }

            // 3. Invalidate the cached token so the new credentials take effect immediately
            var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
            await _authService.InvalidateTokenAsync(repo);

            _logger.LogInformation("Settings saved successfully via Settings page.");
            return RedirectToAction(nameof(Index), new { saved = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save settings.");
            return View("Index", BuildViewModel(error: $"Failed to save settings: {ex.Message}"));
        }
    }

    /// <summary>
    /// Tests a Laserfiche connection using the credentials submitted in the form
    /// without saving anything. Returns an HTML partial for inline injection.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> TestConnection(
        [FromForm] TestConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ServerUrl) ||
            string.IsNullOrWhiteSpace(request.RepositoryId) ||
            string.IsNullOrWhiteSpace(request.Username) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            var failure = ConnectionStatus.Failure(
                "All four fields (Server URL, Repository ID, Username, Password) " +
                "must be filled in to test the connection.");
            return PartialView("_TestResult", failure);
        }

        var status = await _repositoryService.TestConnectionWithCredentialsAsync(
            request.ServerUrl.Trim(),
            request.RepositoryId.Trim(),
            request.Username.Trim(),
            request.Password,
            cancellationToken);

        return PartialView("_TestResult", status);
    }

    // ─────────────────────── Helpers ──────────────────────────────────────────

    private SettingsViewModel BuildViewModel(bool saved = false, string? error = null)
    {
        var opts = _optionsMonitor.CurrentValue;
        return new SettingsViewModel
        {
            ServerUrl                      = opts.ServerUrl,
            RepositoryId                   = opts.RepositoryId,
            DisplayName                    = opts.DisplayName,
            HasSavedCredentials            = _portalConfig.HasSavedCredentials(),
            HasEnvironmentVariableCredentials = _portalConfig.HasEnvironmentVariableCredentials(),
            SaveSuccess                    = saved,
            ErrorMessage                   = error
        };
    }
}

// ── View models & request models ──────────────────────────────────────────────

/// <summary>View model for the Settings page.</summary>
public sealed class SettingsViewModel
{
    /// <summary>Current Laserfiche API Server URL.</summary>
    public string ServerUrl { get; init; } = string.Empty;

    /// <summary>Current repository identifier.</summary>
    public string RepositoryId { get; init; } = string.Empty;

    /// <summary>Human-readable display name for the repository.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>True when a secure credential file exists for the default repository.</summary>
    public bool HasSavedCredentials { get; init; }

    /// <summary>True when both <c>LF_USERNAME</c> and <c>LF_PASSWORD</c> env vars are set.</summary>
    public bool HasEnvironmentVariableCredentials { get; init; }

    /// <summary>True when this page was rendered after a successful save.</summary>
    public bool SaveSuccess { get; init; }

    /// <summary>Error message to display. Null on success.</summary>
    public string? ErrorMessage { get; init; }
}

/// <summary>Form model for the Save action.</summary>
public sealed class SaveSettingsRequest
{
    /// <summary>Laserfiche API Server base URL.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Repository identifier.</summary>
    public string RepositoryId { get; set; } = string.Empty;

    /// <summary>Optional display name. Defaults to RepositoryId if blank.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Laserfiche username. Leave blank to keep existing stored credentials.</summary>
    public string? Username { get; set; }

    /// <summary>Laserfiche password. Required only when Username is provided.</summary>
    public string? Password { get; set; }
}

/// <summary>Form model for the TestConnection action.</summary>
public sealed class TestConnectionRequest
{
    /// <summary>Laserfiche API Server base URL to test against.</summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Repository ID to test.</summary>
    public string RepositoryId { get; set; } = string.Empty;

    /// <summary>Laserfiche username for the test.</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>Laserfiche password for the test.</summary>
    public string Password { get; set; } = string.Empty;
}
