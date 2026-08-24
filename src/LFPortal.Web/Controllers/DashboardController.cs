using System.Diagnostics;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Adapters;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Displays live Laserfiche repository statistics. Repository data is supplied by
/// <see cref="ILaserficheDashboardService"/>.
/// </summary>
public sealed class DashboardController : Controller
{
    private readonly ILaserficheDashboardService _dashboardService;
    private readonly ILogger<DashboardController> _logger;

    public DashboardController(
        ILaserficheDashboardService dashboardService,
        ILogger<DashboardController> logger)
    {
        _dashboardService = dashboardService;
        _logger = logger;
    }

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
    /// Diagnostic probe for the endpoints used by the dashboard. Root entry discovery
    /// follows the exact same dynamic path as the live dashboard.
    /// </summary>
    [HttpGet("/Dashboard/Probe")]
    public async Task<IActionResult> Probe(
        [FromServices] IHttpClientFactory httpClientFactory,
        [FromServices] IRepositoryContext repositoryContext,
        [FromServices] ILaserficheApiAdapter adapter,
        [FromServices] ILaserficheEntryService entryService,
        [FromServices] ICredentialProvider credentialProvider,
        CancellationToken cancellationToken)
    {
        var repo = await repositoryContext.GetActiveRepositoryAsync(cancellationToken);
        var repoId = repo.RepositoryId;

        string? username = null;
        string? credError = null;
        try
        {
            var creds = await credentialProvider.GetCredentialsAsync(repo.Key, cancellationToken);
            username = creds.Username;
        }
        catch (Exception ex)
        {
            credError = ex.Message;
        }

        using var client = httpClientFactory.CreateClient("LaserficheAuthenticated");
        var probes = new List<ProbeResult>();

        int rootId;
        string rootDiscoveryNote;
        try
        {
            rootId = await entryService.GetRootEntryIdAsync(cancellationToken);
            rootDiscoveryNote = $"Discovered from repository path \\ (EntryId={rootId})";
        }
        catch (Exception ex)
        {
            rootId = 0;
            rootDiscoveryNote = $"Root discovery failed: {ex.Message}";
        }

        var urls = new List<(string Label, string Url, bool NoTruncate)>
        {
            ("GET /Repositories", adapter.BuildRepositoriesUrl(), false),
            ("GET /TemplateDefinitions", adapter.BuildTemplateDefinitionsUrl(repoId), false)
        };

        if (rootId > 0)
        {
            urls.Insert(1, (
                $"GET /Entries/{rootId} (discovered root entry details)",
                adapter.BuildEntryUrl(repoId, rootId, EntryResource.Details),
                false));
            urls.Insert(2, (
                $"GET /Entries/{rootId}/Folder/Children [FIRST PAGE RAW]",
                adapter.BuildFolderChildrenUrl(repoId, rootId),
                true));
        }

        foreach (var (label, url, noTruncate) in urls)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                using var response = await client.GetAsync(url, cancellationToken);
                sw.Stop();
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                var contentType = response.Content.Headers.ContentType?.ToString() ?? string.Empty;

                probes.Add(new ProbeResult
                {
                    Label = label,
                    Url = url,
                    StatusCode = (int)response.StatusCode,
                    Status = response.ReasonPhrase ?? response.StatusCode.ToString(),
                    IsSuccess = response.IsSuccessStatusCode,
                    ContentType = contentType,
                    Body = noTruncate ? body : (body.Length > 3000 ? body[..3000] + "\n…[truncated]" : body),
                    ElapsedMs = sw.ElapsedMilliseconds
                });
            }
            catch (Exception ex)
            {
                sw.Stop();
                probes.Add(new ProbeResult
                {
                    Label = label,
                    Url = url,
                    StatusCode = 0,
                    Status = "Exception",
                    IsSuccess = false,
                    Body = ex.ToString(),
                    ElapsedMs = sw.ElapsedMilliseconds
                });
            }
        }

        return View("Probe", new ProbeViewModel
        {
            ServerUrl = repo.ServerUrl,
            RepositoryId = repoId,
            Username = username,
            CredError = credError,
            Probes = probes,
            RootEntryId = rootId,
            RootDiscoveryNote = rootDiscoveryNote
        });
    }
}

public sealed class DashboardViewModel
{
    public DashboardStatsDto Stats { get; init; } = new();
}

public sealed class ProbeViewModel
{
    public string ServerUrl { get; init; } = string.Empty;
    public string RepositoryId { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string? CredError { get; init; }
    public int RootEntryId { get; init; }
    public string RootDiscoveryNote { get; init; } = string.Empty;
    public List<ProbeResult> Probes { get; init; } = [];
}

public sealed class ProbeResult
{
    public string Label { get; init; } = string.Empty;
    public string Url { get; init; } = string.Empty;
    public int StatusCode { get; init; }
    public string Status { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string ContentType { get; init; } = string.Empty;
    public string Body { get; init; } = string.Empty;
    public long ElapsedMs { get; init; }
}
