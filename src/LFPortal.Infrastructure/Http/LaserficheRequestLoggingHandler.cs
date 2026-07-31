using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Http;

/// <summary>
/// Logs the URL inputs immediately before each Laserfiche HTTP request is sent.
/// No credentials or authorization headers are logged.
/// </summary>
internal sealed class LaserficheRequestLoggingHandler : DelegatingHandler
{
    private readonly IOptionsMonitor<LaserficheOptions> _optionsMonitor;
    private readonly ILogger<LaserficheRequestLoggingHandler> _logger;

    public LaserficheRequestLoggingHandler(
        IOptionsMonitor<LaserficheOptions> optionsMonitor,
        ILogger<LaserficheRequestLoggingHandler> logger)
    {
        _optionsMonitor = optionsMonitor;
        _logger = logger;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var options = _optionsMonitor.CurrentValue;

        _logger.LogInformation(
            "Laserfiche request: ServerUrl={ServerUrl}, ApiBasePath={ApiBasePath}, " +
            "Final Request URL={FinalRequestUrl}",
            options.ServerUrl,
            options.ApiBasePath,
            request.RequestUri);

        return base.SendAsync(request, cancellationToken);
    }
}