using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Adapters;
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
        _logger.LogInformation("Dashboard: fetching statistics.");
        var stats = await _dashboardService.GetDashboardStatsAsync(cancellationToken);
        _logger.LogInformation(
            "Dashboard stats: connected={Connected}, docs={Docs}, folders={Folders}, " +
            "templates={Tmpls}, rootFolders={RF}, recentDocs={RD}.",
            stats.IsConnected, stats.TotalDocuments, stats.TotalFolders,
            stats.TotalTemplates, stats.RootFolders.Count, stats.RecentDocs.Count);

        return View(new DashboardViewModel { Stats = stats });
    }

    /// <summary>
    /// Diagnostic probe: fires every Laserfiche API endpoint the dashboard uses and
    /// returns a detailed HTML report showing each request URL, HTTP status, and the
    /// first 2000 characters of the raw response body. Use this to identify exactly
    /// which API calls fail or return unexpected data.
    /// </summary>
    [HttpGet("/Dashboard/Probe")]
    public async Task<IActionResult> Probe(
        [FromServices] IHttpClientFactory     httpClientFactory,
        [FromServices] IRepositoryContext      repositoryContext,
        [FromServices] ILaserficheApiAdapter   adapter,
        [FromServices] ICredentialProvider     credentialProvider,
        CancellationToken cancellationToken)
    {
        var repo = await repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        var repoId = repo.RepositoryId;

        // Credential check (shown in report but not logged as a secret)
        string? username = null;
        string? credError = null;
        try
        {
            var creds = await credentialProvider.GetCredentialsAsync(repo.Key, cancellationToken);
            username = creds.Username;
        }
        catch (Exception ex) { credError = ex.Message; }

        using var client = httpClientFactory.CreateClient("LaserficheAuthenticated");

        var probes = new List<ProbeResult>();

        // Build the list of URLs to test
        var childrenBase    = adapter.BuildEntryUrl(repoId, 1, EntryResource.Children);
        var folderTypedUrl  = adapter.BuildFolderChildrenUrl(repoId, 1);
        var childrenFull    = $"{childrenBase}?$top=20&$count=true";
        var childrenSimple  = childrenBase;

        var urls = new (string Label, string Url)[]
        {
            ("GET /Repositories",                        adapter.BuildRepositoriesUrl()),
            ("GET /Entries/1 (root entry details)",      adapter.BuildEntryUrl(repoId, 1, EntryResource.Details)),
            ("GET /Entries/1/Laserfiche.Repository.Folder/children?$top=20 (OData-typed)", folderTypedUrl.Replace("$top=1000","$top=20")),
            ("GET /Entries/1/children?$top=20&$count=true (plain)", childrenFull),
            ("GET /Entries/1/children (no params)",      childrenSimple),
            ("GET /TemplateDefinitions",                 adapter.BuildTemplateDefinitionsUrl(repoId)),
        };

        foreach (var (label, url) in urls)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                sw.Stop();
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                probes.Add(new ProbeResult
                {
                    Label      = label,
                    Url        = url,
                    StatusCode = (int)response.StatusCode,
                    Status     = response.ReasonPhrase ?? response.StatusCode.ToString(),
                    IsSuccess  = response.IsSuccessStatusCode,
                    Body       = body.Length > 3000 ? body[..3000] + "\n…[truncated]" : body,
                    ElapsedMs  = sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                probes.Add(new ProbeResult
                {
                    Label      = label,
                    Url        = url,
                    StatusCode = 0,
                    Status     = "Exception",
                    IsSuccess  = false,
                    Body       = ex.ToString(),
                    ElapsedMs  = sw.ElapsedMilliseconds
                });
            }
        }

        var vm = new ProbeViewModel
        {
            ServerUrl   = repo.ServerUrl,
            RepositoryId = repoId,
            Username    = username,
            CredError   = credError,
            Probes      = probes
        };

        return View("Probe", vm);
    }
}

/// <summary>View model for the Dashboard index page.</summary>
public sealed class DashboardViewModel
{
    /// <summary>Aggregated live Laserfiche statistics. Never null; check <see cref="DashboardStatsDto.IsConnected"/>.</summary>
    public DashboardStatsDto Stats { get; init; } = new();
}

/// <summary>View model for the diagnostic probe page.</summary>
public sealed class ProbeViewModel
{
    public string   ServerUrl    { get; init; } = "";
    public string   RepositoryId { get; init; } = "";
    public string?  Username     { get; init; }
    public string?  CredError    { get; init; }
    public List<ProbeResult> Probes { get; init; } = [];
}

/// <summary>Single raw HTTP probe result.</summary>
public sealed class ProbeResult
{
    public string Label      { get; init; } = "";
    public string Url        { get; init; } = "";
    public int    StatusCode { get; init; }
    public string Status     { get; init; } = "";
    public bool   IsSuccess  { get; init; }
    public string Body       { get; init; } = "";
    public long   ElapsedMs  { get; init; }
}
