using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Displays the live Laserfiche repository dashboard — entry counts, entry-type
/// breakdown, and the ten most recently modified entries.
/// All data is sourced from <see cref="ILaserficheDashboardService"/>, which
/// aggregates multiple Laserfiche API calls and always returns a populated DTO
/// rather than propagating exceptions.
/// </summary>
public sealed class DashboardController : Controller
{
    private readonly ILaserficheDashboardService _dashboardService;
    private readonly ILogger<DashboardController> _logger;

    /// <summary>Initialises the controller with the required services.</summary>
    public DashboardController(
        ILaserficheDashboardService dashboardService,
        ILogger<DashboardController> logger)
    {
        _dashboardService = dashboardService;
        _logger           = logger;
    }

    /// <summary>
    /// Renders the Dashboard page with live Laserfiche repository statistics.
    /// If Laserfiche is unreachable the page renders an error card instead of crashing.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching dashboard statistics.");
        var stats = await _dashboardService.GetDashboardStatsAsync(cancellationToken);

        return View(new DashboardViewModel { Stats = stats });
    }
}

/// <summary>View model for the Dashboard page.</summary>
public sealed class DashboardViewModel
{
    /// <summary>Aggregated live Laserfiche statistics. Never null; check <see cref="DashboardStatsDto.IsConnected"/>.</summary>
    public DashboardStatsDto Stats { get; init; } = new();
}
