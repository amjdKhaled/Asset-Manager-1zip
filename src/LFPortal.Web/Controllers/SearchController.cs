using LFPortal.Application.Interfaces;
using LFPortal.Domain.Common;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Exposes the Laserfiche repository search functionality as a user-facing page.
/// Delegates all search execution to <see cref="ILaserficheSearchService"/>;
/// records every successful search in <see cref="ISearchAuditLog"/> so the
/// Dashboard search-activity chart stays up to date.
/// </summary>
public sealed class SearchController : Controller
{
    private readonly ILaserficheSearchService          _searchService;
    private readonly ILaserficheTemplateService        _templateService;
    private readonly ILaserficheFieldDefinitionService _fieldDefService;
    private readonly ISearchAuditLog                   _auditLog;
    private readonly ILogger<SearchController>         _logger;

    public SearchController(
        ILaserficheSearchService          searchService,
        ILaserficheTemplateService        templateService,
        ILaserficheFieldDefinitionService fieldDefService,
        ISearchAuditLog                   auditLog,
        ILogger<SearchController>         logger)
    {
        _searchService   = searchService;
        _templateService = templateService;
        _fieldDefService = fieldDefService;
        _auditLog        = auditLog;
        _logger          = logger;
    }

    // GET /Search
    // GET /Search?mode=Simple&query=test&page=2
    // GET /Search?mode=Template&templateName=Invoice&page=1
    // GET /Search?mode=Field&fieldName=Department&fieldValue=Finance&page=1
    // GET /Search?mode=Advanced&query={LF:Name}="Report*"&page=1
    public async Task<IActionResult> Index(
        SearchMode        mode         = SearchMode.Simple,
        string            query        = "",
        string            templateName = "",
        string            fieldName    = "",
        string            fieldValue   = "",
        int               page         = 1,
        CancellationToken cancellationToken = default)
    {
        if (page < 1) page = 1;

        // ── Load dropdown data (non-fatal — empty if Laserfiche unreachable) ──
        var (templates, fields) = await LoadDropdownsAsync(cancellationToken)
            .ConfigureAwait(false);

        var baseModel = new SearchViewModel
        {
            Mode             = mode,
            Query            = query,
            TemplateName     = templateName,
            FieldName        = fieldName,
            FieldValue       = fieldValue,
            Page             = page,
            AvailableTemplates = templates,
            AvailableFields    = fields
        };

        // ── No search input — just show the form ─────────────────────────────
        if (!HasSearchInput(baseModel))
            return View(baseModel);

        // ── Execute the search ───────────────────────────────────────────────
        try
        {
            var results = await ExecuteSearchAsync(baseModel, cancellationToken)
                .ConfigureAwait(false);

            // Record every successful search in the audit log.
            var auditQuery = BuildAuditQuery(baseModel);
            await _auditLog.RecordSearchAsync(auditQuery, cancellationToken)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Search completed: mode={Mode} query={Query} total={Total} page={Page}.",
                mode, auditQuery, results.TotalCount, page);

            return View(baseModel with { Results = results, HasSearched = true });
        }
        catch (LaserficheException ex) when (ex.StatusCode is 401 or 403)
        {
            _logger.LogWarning(
                "Search auth failure: mode={Mode} HTTP {Status}.", mode, ex.StatusCode);

            return View(baseModel with
            {
                HasSearched  = true,
                IsAuthError  = true,
                ErrorMessage = "Laserfiche authentication has expired or is not authorised. " +
                               "Please check Settings."
            });
        }
        catch (TimeoutException ex)
        {
            _logger.LogWarning(
                "Search timed out: mode={Mode} query={Query}.", mode, query);

            return View(baseModel with
            {
                HasSearched = true,
                IsTimeout   = true,
                ErrorMessage = ex.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Search failed: mode={Mode} query={Query}.", mode, query);

            return View(baseModel with
            {
                HasSearched  = true,
                ErrorMessage = "The search could not be completed. " +
                               "Please check the Laserfiche connection in Settings."
            });
        }
    }

    // ──────────────────────────── Private helpers ─────────────────────────────

    private static bool HasSearchInput(SearchViewModel m) => m.Mode switch
    {
        SearchMode.Simple   => !string.IsNullOrWhiteSpace(m.Query),
        SearchMode.Advanced => !string.IsNullOrWhiteSpace(m.Query),
        SearchMode.Template => !string.IsNullOrWhiteSpace(m.TemplateName),
        SearchMode.Field    => !string.IsNullOrWhiteSpace(m.FieldName) &&
                               !string.IsNullOrWhiteSpace(m.FieldValue),
        _                   => false
    };

    private Task<PagedResult<LFSearchResult>> ExecuteSearchAsync(
        SearchViewModel   m,
        CancellationToken cancellationToken) => m.Mode switch
    {
        SearchMode.Simple   => _searchService.SimpleSearchAsync(
                                   m.Query, m.Page, SearchViewModel.DefaultPageSize,
                                   cancellationToken),
        SearchMode.Advanced => _searchService.AdvancedSearchAsync(
                                   m.Query, m.Page, SearchViewModel.DefaultPageSize,
                                   cancellationToken),
        SearchMode.Template => _searchService.SearchByTemplateAsync(
                                   m.TemplateName, m.Page, SearchViewModel.DefaultPageSize,
                                   cancellationToken),
        SearchMode.Field    => _searchService.SearchByFieldAsync(
                                   m.FieldName, m.FieldValue, m.Page,
                                   SearchViewModel.DefaultPageSize, cancellationToken),
        _                   => Task.FromResult(PagedResult<LFSearchResult>.Empty)
    };

    private static string BuildAuditQuery(SearchViewModel m) => m.Mode switch
    {
        SearchMode.Simple   => m.Query,
        SearchMode.Advanced => m.Query,
        SearchMode.Template => $"template:{m.TemplateName}",
        SearchMode.Field    => $"{m.FieldName}={m.FieldValue}",
        _                   => m.Query
    };

    /// <summary>
    /// Loads template and field names for the search form dropdowns.
    /// Both calls are non-fatal: if Laserfiche is unreachable the dropdowns
    /// will be empty and the user can still type values manually.
    /// </summary>
    private async Task<(IReadOnlyList<string> Templates, IReadOnlyList<string> Fields)>
        LoadDropdownsAsync(CancellationToken cancellationToken)
    {
        IReadOnlyList<string> templates = [];
        IReadOnlyList<string> fields    = [];

        var templateTask = _templateService
            .GetTemplateDefinitionsAsync(cancellationToken);
        var fieldTask = _fieldDefService
            .GetFieldDefinitionsAsync(cancellationToken);

        try
        {
            var defs = await templateTask.ConfigureAwait(false);
            templates = defs
                .Select(t => t.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Template definitions unavailable; dropdown will be empty.");
        }

        try
        {
            var defs = await fieldTask.ConfigureAwait(false);
            fields = defs.Values
                .Select(f => f.Name)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList()
                .AsReadOnly();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Field definitions unavailable; dropdown will be empty.");
        }

        return (templates, fields);
    }
}
