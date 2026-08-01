using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Legacy entry point kept for backward compatibility.
/// All requests to <c>/</c> and <c>/Home</c> are redirected to the Dashboard.
/// </summary>
/// <remarks>
/// Connection status information previously shown on this page has been moved to
/// the Settings page (<c>/Settings</c>), where it is displayed alongside the
/// connection configuration as a "Connection Status" section.
/// </remarks>
public sealed class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    /// <summary>Initialises the controller with a logger.</summary>
    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    /// <summary>Redirects to the Dashboard (legacy root URL support).</summary>
    [HttpGet]
    public IActionResult Index()
    {
        return RedirectToAction("Index", "Dashboard");
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
