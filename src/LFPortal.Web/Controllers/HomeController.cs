using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Version;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Handles the portal home (status) page.
/// Performs a live Laserfiche connection check on every page load so that
/// administrators can immediately see whether the portal can reach the API Server.
/// </summary>
public sealed class HomeController : Controller
{
    private readonly ILaserficheRepositoryService _repositoryService;
    private readonly ILogger<HomeController> _logger;

    /// <summary>Initialises the controller with the required services.</summary>
    public HomeController(
        ILaserficheRepositoryService repositoryService,
        ILogger<HomeController> logger)
    {
        _repositoryService = repositoryService;
        _logger            = logger;
    }

    /// <summary>
    /// Displays the LFPortal status page, including the result of a live
    /// Laserfiche connection check.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var status = await _repositoryService
            .TestConnectionAsync(cancellationToken);

        var model = new HomeViewModel
        {
            Version   = LFPortalVersion.Display,
            Status    = status,
            CheckedAt = DateTimeOffset.UtcNow
        };

        return View(model);
    }

    /// <summary>
    /// Displays a minimal error page. Used by the production exception handler.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        _logger.LogWarning("Error page rendered for request {TraceId}.", HttpContext.TraceIdentifier);
        return View();
    }
}

/// <summary>
/// View model for the LFPortal home/status page.
/// </summary>
public sealed class HomeViewModel
{
    /// <summary>Portal version string, e.g. <c>LFPortal v1.0.0</c>.</summary>
    public string Version { get; init; } = string.Empty;

    /// <summary>Result of the live Laserfiche connection check.</summary>
    public ConnectionStatus Status { get; init; } = ConnectionStatus.Failure("Not checked");

    /// <summary>UTC timestamp when this view model was populated.</summary>
    public DateTimeOffset CheckedAt { get; init; }
}
