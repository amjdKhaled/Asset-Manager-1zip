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

        // ── Step 0: Discover the actual repository root entry ID ──────────────
        // Do NOT assume root = 1. Use path-based lookup (entryPath=\) first.
        int discoveredRootId = 1;
        string rootDiscoveryNote = "";
        var rootPathUrl = $"{adapter.BuildEntriesUrl(repoId)}?entryPath=%5C&fallbackToClosestAncestor=false";
        {
            var sw0 = Stopwatch.StartNew();
            try
            {
                using var r = await client.GetAsync(rootPathUrl, cancellationToken);
                sw0.Stop();
                var body = await r.Content.ReadAsStringAsync(cancellationToken);
                probes.Add(new ProbeResult
                {
                    Label      = "GET /Entries?entryPath=%5C (root discovery)",
                    Url        = rootPathUrl,
                    StatusCode = (int)r.StatusCode,
                    Status     = r.ReasonPhrase ?? r.StatusCode.ToString(),
                    IsSuccess  = r.IsSuccessStatusCode,
                    Body       = body.Length > 3000 ? body[..3000] + "\n…[truncated]" : body,
                    ElapsedMs  = sw0.ElapsedMilliseconds
                });
                if (r.IsSuccessStatusCode)
                {
                    // Try to extract the id field from the single-entry JSON response
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int rid) && rid > 0)
                    {
                        discoveredRootId = rid;
                        rootDiscoveryNote = $"Root discovered via path lookup: ID={rid}";
                    }
                }
            }
            catch (Exception ex)
            {
                sw0.Stop();
                probes.Add(new ProbeResult
                {
                    Label = "GET /Entries?entryPath=%5C (root discovery)",
                    Url = rootPathUrl, StatusCode = 0, Status = "Exception",
                    IsSuccess = false, Body = ex.ToString(), ElapsedMs = sw0.ElapsedMilliseconds
                });
            }
        }

        // If path lookup didn't find a root, check entry 1
        if (discoveredRootId == 1 && string.IsNullOrEmpty(rootDiscoveryNote))
        {
            var sw1 = Stopwatch.StartNew();
            var entry1Url = adapter.BuildEntryUrl(repoId, 1, EntryResource.Details);
            try
            {
                using var r = await client.GetAsync(entry1Url, cancellationToken);
                sw1.Stop();
                var body = await r.Content.ReadAsStringAsync(cancellationToken);
                probes.Add(new ProbeResult
                {
                    Label      = "GET /Entries/1 (is it the root? parentId should be 0)",
                    Url        = entry1Url,
                    StatusCode = (int)r.StatusCode,
                    Status     = r.ReasonPhrase ?? r.StatusCode.ToString(),
                    IsSuccess  = r.IsSuccessStatusCode,
                    Body       = body.Length > 3000 ? body[..3000] + "\n…[truncated]" : body,
                    ElapsedMs  = sw1.ElapsedMilliseconds
                });
                if (r.IsSuccessStatusCode)
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if (doc.RootElement.TryGetProperty("parentId", out var pidEl) && pidEl.TryGetInt32(out int pid))
                        rootDiscoveryNote = pid == 0 ? "Entry 1 is the root (parentId=0)" : $"Entry 1 is NOT root (parentId={pid})";
                }
            }
            catch (Exception ex)
            {
                sw1.Stop();
                probes.Add(new ProbeResult
                {
                    Label = "GET /Entries/1 (root check)", Url = adapter.BuildEntryUrl(repoId, 1, EntryResource.Details),
                    StatusCode = 0, Status = "Exception", IsSuccess = false, Body = ex.ToString(), ElapsedMs = sw1.ElapsedMilliseconds
                });
            }
        }

        // Build the remaining probes using the discovered root ID
        var folderChildrenUrl = adapter.BuildFolderChildrenUrl(repoId, discoveredRootId).Replace("$top=1000", "$top=20");
        var childrenBase      = adapter.BuildEntryUrl(repoId, discoveredRootId, EntryResource.Children);

        var urls = new (string Label, string Url)[]
        {
            ("GET /Repositories",                                    adapter.BuildRepositoriesUrl()),
            ($"GET /Entries/{discoveredRootId} (root entry details)", adapter.BuildEntryUrl(repoId, discoveredRootId, EntryResource.Details)),
            ($"GET /Entries/{discoveredRootId}/Laserfiche.Repository.Folder/children?$top=20 [DASHBOARD PRIMARY]", folderChildrenUrl),
            ($"GET /Entries/{discoveredRootId}/children?$top=20 [fallback]", $"{childrenBase}?$top=20"),
            ("GET /TemplateDefinitions",                              adapter.BuildTemplateDefinitionsUrl(repoId)),
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
            ServerUrl    = repo.ServerUrl,
            RepositoryId = repoId,
            Username     = username,
            CredError    = credError,
            Probes       = probes,
            RootEntryId  = discoveredRootId,
            RootDiscoveryNote = string.IsNullOrEmpty(rootDiscoveryNote)
                ? $"Defaulting to ID={discoveredRootId} (discovery inconclusive)"
                : rootDiscoveryNote
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
    public string Label      { get; init; } = "";
    public string Url        { get; init; } = "";
    public int    StatusCode { get; init; }
    public string Status     { get; init; } = "";
    public bool   IsSuccess  { get; init; }
    public string Body       { get; init; } = "";
    public long   ElapsedMs  { get; init; }
}
