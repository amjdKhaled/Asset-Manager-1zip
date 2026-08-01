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

        // ── Root entry ID — use configured value directly (no ByPath network call needed) ──
        // RootEntryId defaults to 1 in LaserficheOptions and can be overridden in appsettings.json.
        int rootId = adapter.GetConfiguredRootEntryId();
        string rootDiscoveryNote = $"From configuration (RootEntryId={rootId})";

        // ── Probe list ────────────────────────────────────────────────────────
        // The folder-children probe body is NOT truncated — we need the complete raw response.
        var folderChildrenUrl = adapter.BuildFolderChildrenUrl(repoId, rootId);

        var urls = new (string Label, string Url, bool NoTruncate)[]
        {
            ("GET /Repositories",
                adapter.BuildRepositoriesUrl(), false),
            ($"GET /Entries/{rootId} (root entry details)",
                adapter.BuildEntryUrl(repoId, rootId, EntryResource.Details), false),
            ($"GET /Entries/{rootId}/Laserfiche.Repository.Folder/children [COMPLETE RAW]",
                folderChildrenUrl, true),
            ("GET /TemplateDefinitions",
                adapter.BuildTemplateDefinitionsUrl(repoId), false),
        };

        foreach (var (label, url, noTruncate) in urls)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                sw.Stop();
                // Read raw body BEFORE any processing — no DTO conversion.
                var body        = await response.Content.ReadAsStringAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.ToString() ?? "";
                probes.Add(new ProbeResult
                {
                    Label       = label,
                    Url         = url,
                    StatusCode  = (int)response.StatusCode,
                    Status      = response.ReasonPhrase ?? response.StatusCode.ToString(),
                    IsSuccess   = response.IsSuccessStatusCode,
                    ContentType = contentType,
                    // noTruncate = true means show the complete raw body (used for folder-children).
                    Body        = noTruncate ? body : (body.Length > 3000 ? body[..3000] + "\n…[truncated]" : body),
                    ElapsedMs   = sw.ElapsedMilliseconds
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
            ServerUrl         = repo.ServerUrl,
            RepositoryId      = repoId,
            Username          = username,
            CredError         = credError,
            Probes            = probes,
            RootEntryId       = rootId,
            RootDiscoveryNote = rootDiscoveryNote
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
    public string   ServerUrl         { get; init; } = "";
    public string   RepositoryId      { get; init; } = "";
    public string?  Username          { get; init; }
    public string?  CredError         { get; init; }
    public int      RootEntryId       { get; init; } = 1;
    public string   RootDiscoveryNote { get; init; } = "";
    public List<ProbeResult> Probes   { get; init; } = [];
}

/// <summary>Single raw HTTP probe result.</summary>
public sealed class ProbeResult
{
    public string Label       { get; init; } = "";
    public string Url         { get; init; } = "";
    public int    StatusCode  { get; init; }
    public string Status      { get; init; } = "";
    public bool   IsSuccess   { get; init; }
    public string ContentType { get; init; } = "";
    public string Body        { get; init; } = "";
    public long   ElapsedMs   { get; init; }
}
