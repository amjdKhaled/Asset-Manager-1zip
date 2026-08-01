using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Provides the Settings page for configuring the Laserfiche connection, storing
/// credentials, testing the connection, and viewing live connection status.
/// All connection logic is delegated to Application-layer services — no HTTP calls
/// in this controller beyond delegating to service methods.
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

    /// <summary>
    /// Displays the Settings page, pre-populated with the current configuration and
    /// a live Laserfiche connection status check.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(
        bool saved = false,
        CancellationToken cancellationToken = default)
    {
        // Run a live connection test so the Connection Status section is current.
        ConnectionStatus? status = null;
        try
        {
            status = await _repositoryService.TestConnectionAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Settings: connection test failed while loading page.");
            status = ConnectionStatus.Failure($"Connection test could not complete: {ex.Message}");
        }

        var activeRepoId     = HttpContext.Session.GetString("ActiveRepositoryId");
        var activeRepoSource = HttpContext.Session.GetString("ActiveRepositorySource");

        return View(BuildViewModel(saved: saved, status: status,
            activeRepoId: activeRepoId, activeRepoSource: activeRepoSource));
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
        // Normalise and validate
        var (serverUrl, apiBasePath, apiVersion) = NormaliseServerUrl(
            request.ServerUrl?.Trim() ?? string.Empty,
            request.ApiBasePath?.Trim() ?? "/LFRepositoryAPI",
            request.ApiVersion?.Trim() ?? "v1");

        if (string.IsNullOrWhiteSpace(serverUrl))
            ModelState.AddModelError(nameof(request.ServerUrl), "Server URL is required.");
        if (string.IsNullOrWhiteSpace(request.RepositoryId))
            ModelState.AddModelError(nameof(request.RepositoryId), "Default Repository is required.");
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
            // 1. Persist connection settings
            await _portalConfig.SaveConnectionSettingsAsync(
                serverUrl,
                request.RepositoryId.Trim(),
                request.DisplayName?.Trim() ?? string.Empty,
                apiBasePath,
                apiVersion,
                request.RootEntryId > 0 ? request.RootEntryId : 1,
                request.TimeoutSeconds is >= 5 and <= 300 ? request.TimeoutSeconds : 30,
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

            _logger.LogInformation(
                "Settings saved: ServerUrl={ServerUrl}, ApiBasePath={ApiBasePath}, ApiVersion={ApiVersion}.",
                serverUrl, apiBasePath, apiVersion);

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
                "All four fields (Server URL, Default Repository, Username, Password) " +
                "must be filled in to test the connection.");
            return PartialView("_TestResult", failure);
        }

        // Normalise the URL the user typed — extract ApiBasePath/ApiVersion if they
        // accidentally included them in the Server URL field.
        var (serverUrl, _, _) = NormaliseServerUrl(
            request.ServerUrl.Trim(),
            request.ApiBasePath?.Trim() ?? "/LFRepositoryAPI",
            "v1");

        // Temporarily persist URL/path so the adapter's IOptionsMonitor picks it up
        // for building test request URLs. Credentials are not written.
        var opts = _optionsMonitor.CurrentValue;
        await _portalConfig.SaveConnectionSettingsAsync(
            serverUrl,
            request.RepositoryId.Trim(),
            opts.DisplayName,
            request.ApiBasePath?.Trim() ?? opts.ApiBasePath,
            "v1",
            opts.RootEntryId,
            opts.TimeoutSeconds,
            cancellationToken);

        // Invalidate cached token so the test uses a fresh one against the new URL
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        await _authService.InvalidateTokenAsync(repo);

        var status = await _repositoryService.TestConnectionWithCredentialsAsync(
            serverUrl,
            request.RepositoryId.Trim(),
            request.Username.Trim(),
            request.Password,
            cancellationToken);

        return PartialView("_TestResult", status);
    }

    // ─────────────────────── Helpers ──────────────────────────────────────────

    private SettingsViewModel BuildViewModel(
        bool saved = false,
        string? error = null,
        ConnectionStatus? status = null,
        string? activeRepoId = null,
        string? activeRepoSource = null)
    {
        var opts = _optionsMonitor.CurrentValue;
        return new SettingsViewModel
        {
            ServerUrl                         = opts.ServerUrl,
            RepositoryId                      = opts.RepositoryId,
            DisplayName                       = opts.DisplayName,
            ApiBasePath                       = opts.ApiBasePath,
            ApiVersion                        = opts.ApiVersion,
            RootEntryId                       = opts.RootEntryId,
            TimeoutSeconds                    = opts.TimeoutSeconds,
            HasSavedCredentials               = _portalConfig.HasSavedCredentials(),
            HasEnvironmentVariableCredentials = _portalConfig.HasEnvironmentVariableCredentials(),
            SaveSuccess                       = saved,
            ErrorMessage                      = error,
            ConnectionStatus                  = status,
            ActiveRepositoryId                = activeRepoId,
            ActiveRepositorySource            = activeRepoSource,
        };
    }

    /// <summary>
    /// Discovers available repositories on the specified Laserfiche server using
    /// the credentials provided in the form. Returns JSON so the Settings page
    /// can populate the Repository ID field without a full page reload.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DiscoverRepositories(
        [FromForm] DiscoverRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ServerUrl) ||
            string.IsNullOrWhiteSpace(request.Username)  ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return Json(new { error = "Server URL, Username and Password are required to discover repositories." });
        }

        var (serverUrl, _, _) = NormaliseServerUrl(
            request.ServerUrl.Trim(),
            request.ApiBasePath?.Trim() ?? "/LFRepositoryAPI",
            "v1");

        try
        {
            var repos = await _repositoryService.DiscoverRepositoriesAsync(
                serverUrl,
                request.RepositoryId?.Trim() ?? string.Empty,
                request.Username.Trim(),
                request.Password,
                cancellationToken);

            var items = repos.Select(r => new { id = r.RepositoryId, name = r.RepositoryName ?? r.RepositoryId });
            return Json(new { repositories = items });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Repository discovery failed for {Url}.", serverUrl);
            return Json(new { error = $"Discovery failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Normalises a user-supplied Server URL. If the user pasted a full path such as
    /// <c>https://host/LFRepositoryAPI/v1/Repositories</c>, the method extracts
    /// <c>ApiBasePath</c> and <c>ApiVersion</c> and returns just the scheme+host.
    /// The explicit <paramref name="apiBasePath"/> and <paramref name="apiVersion"/>
    /// overrides from the form take precedence when the URL contains no path clues.
    /// </summary>
    private static (string serverUrl, string apiBasePath, string apiVersion) NormaliseServerUrl(
        string rawUrl, string apiBasePath, string apiVersion)
    {
        if (string.IsNullOrWhiteSpace(rawUrl))
            return (rawUrl, apiBasePath, apiVersion);

        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri))
            return (rawUrl, apiBasePath, apiVersion);

        var segments = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        // Look for a version segment like "v1" or "v2"
        int vIdx = Array.FindIndex(
            segments,
            s => s.StartsWith('v') && s.Length >= 2 && s[1..].All(char.IsDigit));

        if (vIdx >= 0)
        {
            var detectedVersion  = segments[vIdx];
            var detectedBasePath = "/" + string.Join("/", segments[..vIdx]);
            var cleanHost = $"{uri.Scheme}://{uri.Host}"
                + (uri.IsDefaultPort ? "" : $":{uri.Port}");

            return (cleanHost, detectedBasePath, detectedVersion);
        }

        // No version segment — strip any trailing /Repositories path
        int rIdx = Array.FindLastIndex(
            segments,
            s => s.Equals("Repositories", StringComparison.OrdinalIgnoreCase));

        if (rIdx >= 0)
        {
            var cleanHost = $"{uri.Scheme}://{uri.Host}"
                + (uri.IsDefaultPort ? "" : $":{uri.Port}");
            var remaining = segments[..rIdx];
            var detectedBasePath = remaining.Length > 0
                ? "/" + string.Join("/", remaining)
                : apiBasePath;
            return (cleanHost, detectedBasePath, apiVersion);
        }

        // URL looks clean already — return as-is with the form's explicit values
        return (rawUrl.TrimEnd('/'), apiBasePath, apiVersion);
    }
}

// ── View models & request models ──────────────────────────────────────────────

/// <summary>View model for the Settings page.</summary>
public sealed class SettingsViewModel
{
    public string  ServerUrl      { get; init; } = string.Empty;
    public string  RepositoryId   { get; init; } = string.Empty;
    public string  DisplayName    { get; init; } = string.Empty;
    public string  ApiBasePath    { get; init; } = "/LFRepositoryAPI";
    public string  ApiVersion     { get; init; } = "v1";
    public int     RootEntryId    { get; init; } = 1;
    public int     TimeoutSeconds { get; init; } = 30;
    public bool    HasSavedCredentials               { get; init; }
    public bool    HasEnvironmentVariableCredentials { get; init; }
    public bool    SaveSuccess    { get; init; }
    public string? ErrorMessage   { get; init; }

    // ── Live connection status (null when not yet checked or on validation error) ──

    /// <summary>Live connection status. Null on validation error paths.</summary>
    public ConnectionStatus? ConnectionStatus { get; init; }

    /// <summary>Repository ID provided by the Laserfiche Desktop Client session. Null when opening directly in a browser.</summary>
    public string? ActiveRepositoryId { get; init; }

    /// <summary>Source label for the active repository ("Laserfiche Desktop Client" or null).</summary>
    public string? ActiveRepositorySource { get; init; }

    /// <summary>
    /// The effective active repository ID: the Desktop Client override if present,
    /// otherwise the configured default.
    /// </summary>
    public string EffectiveRepositoryId => !string.IsNullOrWhiteSpace(ActiveRepositoryId)
        ? ActiveRepositoryId
        : RepositoryId;

    /// <summary>Human-readable source for the active repository.</summary>
    public string EffectiveRepositorySource => ActiveRepositorySource ?? "Default Configuration";
}

/// <summary>Form model for the Save action.</summary>
public sealed class SaveSettingsRequest
{
    public string  ServerUrl      { get; set; } = string.Empty;
    public string  RepositoryId   { get; set; } = string.Empty;
    public string? DisplayName    { get; set; }
    public string? ApiBasePath    { get; set; }
    public string? ApiVersion     { get; set; }
    public int     RootEntryId    { get; set; } = 1;
    public int     TimeoutSeconds { get; set; } = 30;
    public string? Username       { get; set; }
    public string? Password       { get; set; }
}

/// <summary>Form model for the TestConnection action.</summary>
public sealed class TestConnectionRequest
{
    public string  ServerUrl    { get; set; } = string.Empty;
    public string  RepositoryId { get; set; } = string.Empty;
    public string? ApiBasePath  { get; set; }
    public string? ApiVersion   { get; set; }
    public string  Username     { get; set; } = string.Empty;
    public string  Password     { get; set; } = string.Empty;
}

/// <summary>Form model for the DiscoverRepositories action.</summary>
public sealed class DiscoverRequest
{
    public string  ServerUrl    { get; set; } = string.Empty;
    public string? RepositoryId { get; set; }
    public string? ApiBasePath  { get; set; }
    public string  Username     { get; set; } = string.Empty;
    public string  Password     { get; set; } = string.Empty;
}
