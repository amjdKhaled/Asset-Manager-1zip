using LFPortal.Application.Interfaces;
using LFPortal.Domain.Version;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Provides diagnostic REST API endpoints for integration testing, monitoring scripts,
/// and the IIS Application Request Routing health probe.
/// All responses are JSON. These endpoints have no authentication requirement in Phase 1;
/// network-level access restrictions (IIS IP filtering or firewall rules) should be
/// applied in production environments.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public sealed class LaserficheApiController : ControllerBase
{
    private readonly ILaserficheRepositoryService _repositoryService;
    private readonly IRepositoryContext _repositoryContext;
    private readonly ILogger<LaserficheApiController> _logger;

    /// <summary>Initialises the controller with required services.</summary>
    public LaserficheApiController(
        ILaserficheRepositoryService repositoryService,
        IRepositoryContext repositoryContext,
        ILogger<LaserficheApiController> logger)
    {
        _repositoryService = repositoryService;
        _repositoryContext = repositoryContext;
        _logger            = logger;
    }

    /// <summary>
    /// Returns the portal version and a live Laserfiche connection status.
    /// Used as the primary health and diagnostic endpoint.
    /// </summary>
    /// <response code="200">Connection succeeded.</response>
    /// <response code="503">Laserfiche API Server is unreachable or returned an error.</response>
    [HttpGet("status")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
    {
        var repo   = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        var status = await _repositoryService.TestConnectionAsync(cancellationToken);

        var response = new StatusResponse
        {
            Version        = LFPortalVersion.Full,
            PortalDisplay  = LFPortalVersion.Display,
            IsConnected    = status.IsConnected,
            RepositoryId   = status.RepositoryId ?? repo.RepositoryId,
            RepositoryName = status.RepositoryName,
            ServerVersion  = status.ServerVersion,
            ApiVersion     = status.ApiVersion,
            CheckedAt      = status.CheckedAt,
            ErrorMessage   = status.ErrorMessage
        };

        return status.IsConnected
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    /// <summary>
    /// Tests connectivity using explicitly provided credentials. Useful during initial
    /// configuration to verify that the server URL, repository ID, and credentials are
    /// correct before saving them via the Settings page.
    /// </summary>
    /// <response code="200">Connection succeeded with the provided credentials.</response>
    /// <response code="400">Request body is missing required fields.</response>
    /// <response code="503">Connection failed with the provided credentials.</response>
    [HttpPost("test-connection")]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(StatusResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IActionResult> TestConnection(
        [FromBody] TestConnectionRequest request,
        CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        _logger.LogInformation(
            "Test-connection requested for server {ServerUrl}, repository {RepoId}.",
            request.ServerUrl,
            request.RepositoryId);

        var status = await _repositoryService.TestConnectionWithCredentialsAsync(
            request.ServerUrl,
            request.RepositoryId,
            request.Username,
            request.Password,
            cancellationToken);

        var response = new StatusResponse
        {
            Version        = LFPortalVersion.Full,
            PortalDisplay  = LFPortalVersion.Display,
            IsConnected    = status.IsConnected,
            RepositoryId   = status.RepositoryId ?? request.RepositoryId,
            RepositoryName = status.RepositoryName,
            ServerVersion  = status.ServerVersion,
            ApiVersion     = status.ApiVersion,
            CheckedAt      = status.CheckedAt,
            ErrorMessage   = status.ErrorMessage
        };

        return status.IsConnected
            ? Ok(response)
            : StatusCode(StatusCodes.Status503ServiceUnavailable, response);
    }

    /// <summary>
    /// Returns the currently active repository descriptor.
    /// Used by administrators to confirm which repository the portal is configured against.
    /// </summary>
    [HttpGet("repository")]
    [ProducesResponseType(typeof(RepositoryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetRepository(CancellationToken cancellationToken)
    {
        var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken);

        return Ok(new RepositoryResponse
        {
            RepositoryId  = repo.RepositoryId,
            DisplayName   = repo.DisplayName
        });
    }

    // ──────────────────────────── Response / request models ──────────────────

    /// <summary>Response body for status and connection-test endpoints.</summary>
    public sealed class StatusResponse
    {
        /// <summary>Portal semantic version, e.g. <c>1.0.0</c>.</summary>
        public string Version { get; init; } = string.Empty;

        /// <summary>Portal display string, e.g. <c>LFPortal v1.0.0</c>.</summary>
        public string PortalDisplay { get; init; } = string.Empty;

        /// <summary><c>true</c> when Laserfiche is reachable and the repository is accessible.</summary>
        public bool IsConnected { get; init; }

        /// <summary>Repository ID being used.</summary>
        public string? RepositoryId { get; init; }

        /// <summary>Repository display name. Null on failure.</summary>
        public string? RepositoryName { get; init; }

        /// <summary>Laserfiche Server version. Null on failure.</summary>
        public string? ServerVersion { get; init; }

        /// <summary>API version used, e.g. <c>v2</c>. Null on failure.</summary>
        public string? ApiVersion { get; init; }

        /// <summary>UTC time the check was performed.</summary>
        public DateTimeOffset CheckedAt { get; init; }

        /// <summary>Error description when <see cref="IsConnected"/> is <c>false</c>.</summary>
        public string? ErrorMessage { get; init; }
    }

    /// <summary>Response body for the repository descriptor endpoint.</summary>
    public sealed class RepositoryResponse
    {
        /// <summary>Repository ID used in API paths.</summary>
        public string RepositoryId { get; init; } = string.Empty;

        /// <summary>Human-readable repository name shown in the UI.</summary>
        public string DisplayName { get; init; } = string.Empty;
    }

    /// <summary>Request body for the test-connection endpoint.</summary>
    public sealed class TestConnectionRequest
    {
        /// <summary>Base URL of the Laserfiche API Server.</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string ServerUrl { get; init; } = string.Empty;

        /// <summary>Repository identifier to test against.</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string RepositoryId { get; init; } = string.Empty;

        /// <summary>Laserfiche username.</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string Username { get; init; } = string.Empty;

        /// <summary>Laserfiche password.</summary>
        [System.ComponentModel.DataAnnotations.Required]
        public string Password { get; init; } = string.Empty;
    }
}
