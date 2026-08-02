using LFPortal.Infrastructure.Adapters;
using LFPortal.Infrastructure.Options;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Safe, credential-free connectivity diagnostics for the Laserfiche API.
/// </summary>
/// <remarks>
/// <para>
/// <c>GET /api/diagnostics/laserfiche</c> reports the exact URLs the application
/// builds from the live configuration (so administrators can verify that
/// <c>/LFRepositoryAPI</c> appears exactly once) and performs an unauthenticated
/// probe of the Repositories endpoint, classifying any failure
/// (DNS, connection refused, TLS, timeout, HTTP status).
/// </para>
/// <para>
/// No credentials are read, sent, or returned. An HTTP 401 from the server is
/// reported as <c>reachable</c> — it proves the API answered.
/// </para>
/// </remarks>
[ApiController]
[Route("api/diagnostics")]
public sealed class DiagnosticsApiController : ControllerBase
{
    private readonly ILaserficheApiAdapter _adapter;
    private readonly IOptionsMonitor<LaserficheOptions> _options;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DiagnosticsApiController> _logger;

    /// <summary>Initialises the controller.</summary>
    public DiagnosticsApiController(
        ILaserficheApiAdapter adapter,
        IOptionsMonitor<LaserficheOptions> options,
        IHttpClientFactory httpClientFactory,
        ILogger<DiagnosticsApiController> logger)
    {
        _adapter           = adapter;
        _options           = options;
        _httpClientFactory = httpClientFactory;
        _logger            = logger;
    }

    /// <summary>
    /// Reports the configured Laserfiche URLs and probes the server without credentials.
    /// </summary>
    /// <param name="repository">
    /// Optional repository name used only to show the token URL that would be built.
    /// Defaults to the configured fallback repository (if any).
    /// </param>
    [HttpGet("laserfiche")]
    public async Task<IActionResult> Laserfiche(
        [FromQuery] string? repository,
        CancellationToken cancellationToken)
    {
        var opts   = _options.CurrentValue;
        var repoId = string.IsNullOrWhiteSpace(repository) ? opts.RepositoryId : repository.Trim();

        var repositoriesUrl = _adapter.BuildRepositoriesUrl();
        var tokenUrl = string.IsNullOrWhiteSpace(repoId)
            ? null
            : _adapter.BuildTokenUrl(repoId);

        _logger.LogInformation(
            "[LF AUTH] Diagnostics probe: GET {Url} (no credentials).", repositoriesUrl);

        var probe = await ProbeAsync(repositoriesUrl, cancellationToken);

        return Ok(new
        {
            configuration = new
            {
                serverUrl      = opts.ServerUrl,
                apiBasePath    = opts.ApiBasePath,
                apiVersion     = opts.ApiVersion,
                timeoutSeconds = opts.TimeoutSeconds,
                fallbackRepository = string.IsNullOrWhiteSpace(opts.RepositoryId) ? null : opts.RepositoryId
            },
            urls = new
            {
                repositoriesUrl,
                tokenUrl
            },
            probe
        });
    }

    /// <summary>
    /// Performs an unauthenticated GET against the given URL and classifies the outcome.
    /// </summary>
    private async Task<object> ProbeAsync(string url, CancellationToken cancellationToken)
    {
        using var client = _httpClientFactory.CreateClient("LaserficheRaw");

        try
        {
            using var response = await client.GetAsync(
                url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            var status = (int)response.StatusCode;

            // Any HTTP answer proves the server is reachable; 401/403 simply
            // mean the endpoint requires authentication, which is expected here.
            var (result, detail) = status switch
            {
                >= 200 and < 300 => ("reachable", "The Laserfiche API answered successfully."),
                401 or 403       => ("reachable", "The Laserfiche API answered (authentication required, as expected)."),
                404              => ("not-found", "The URL was not found. The API base path may be wrong (check /LFRepositoryAPI)."),
                >= 500           => ("server-error", "The Laserfiche server reported an internal error."),
                _                => ("unexpected-status", "The Laserfiche server returned an unexpected status.")
            };

            _logger.LogInformation(
                "[LF AUTH] Diagnostics probe result: HTTP {Status} ({Result}) for {Url}.",
                status, result, url);

            return new { result, httpStatus = status, detail };
        }
        catch (Exception ex)
        {
            var classification = Diagnostics.LaserficheErrorClassifier.Classify(ex);

            _logger.LogWarning(
                "[LF AUTH] Diagnostics probe failed for {Url}: {Result} ({ErrorType}).",
                url, classification.Code, ex.GetType().Name);

            return new { result = classification.Code, httpStatus = (int?)null, detail = classification.Detail };
        }
    }
}
