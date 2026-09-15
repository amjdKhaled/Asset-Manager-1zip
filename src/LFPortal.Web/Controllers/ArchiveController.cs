using LFPortal.Application.Interfaces;
using LFPortal.Application.DTOs;
using LFPortal.Domain.Entities;
using LFPortal.Infrastructure.Adapters;
using Microsoft.AspNetCore.Mvc;
using System.Net;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Serves the Document Archive browser — a live, folder-by-folder view of the
/// Laserfiche repository. Only the current folder's direct children are fetched
/// on each request; no full recursive scan is performed here.
/// </summary>
public sealed class ArchiveController : Controller
{
    private readonly ILaserficheEntryService            _entryService;
    private readonly ILaserficheFieldDefinitionService  _fieldDefService;
    private readonly ILaserficheSearchService           _searchService;
    private readonly ILaserficheDashboardService        _dashboardService;
    private readonly ILaserficheTemplateService         _templateService;
    private readonly IRepositoryContext                 _repositoryContext;
    private readonly ILaserficheDocumentService         _documentService;
    private readonly ILaserficheApiAdapter              _adapter;
    private readonly ILogger<ArchiveController>         _logger;

    public ArchiveController(
        ILaserficheEntryService           entryService,
        ILaserficheFieldDefinitionService fieldDefService,
        ILaserficheSearchService          searchService,
        ILaserficheDashboardService       dashboardService,
        ILaserficheTemplateService        templateService,
        IRepositoryContext                repositoryContext,
        ILaserficheDocumentService        documentService,
        ILaserficheApiAdapter             adapter,
        ILogger<ArchiveController>        logger)
    {
        _entryService    = entryService;
        _fieldDefService = fieldDefService;
        _searchService = searchService;
        _dashboardService = dashboardService;
        _templateService = templateService;
        _repositoryContext = repositoryContext;
        _documentService = documentService;
        _adapter         = adapter;
        _logger          = logger;
    }

    // GET /Archive          → root
    // GET /Archive?entryId=N&trail=...  → specific folder
    public async Task<IActionResult> Index(
        int    entryId = 0,
        string trail   = "",
        string scope = "",
        string? template = null,
        string? folder = null,
        string? creator = null,
        string? date = null,
        string? activity = null,
        int page = 1,
        CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(scope))
        {
            try
            {
                page = Math.Max(1, page);
                var rootEntryId = _adapter.GetConfiguredRootEntryId();
                if (string.Equals(scope, "folders", StringComparison.OrdinalIgnoreCase) && rootEntryId <= 0)
                    rootEntryId = await _entryService.GetRootEntryIdAsync(cancellationToken).ConfigureAwait(false);
                var query = LaserficheArchiveQuery.Build(
                    scope, template, folder, creator, date, activity, entryId, rootEntryId);
                var repo = await _repositoryContext.GetActiveRepositoryAsync(cancellationToken).ConfigureAwait(false);

                if (query.IsTemplateCatalog)
                {
                    var definitions = await _templateService.GetTemplateDefinitionsAsync(cancellationToken).ConfigureAwait(false);
                    var rows = definitions.Skip((page - 1) * 10).Take(10)
                        .Select(item => new ArchiveTemplateResult { Id = item.Id, Name = item.Name, Description = item.Description })
                        .ToList().AsReadOnly();
                    return View(BuildDrillDownModel(query, repo, rows, [], page, definitions.Count));
                }

                if (query.OpenEntryId > 0)
                {
                    var entry = await _entryService.GetEntryAsync(query.OpenEntryId, cancellationToken).ConfigureAwait(false);
                    return View(BuildDrillDownModel(query, repo, [], [entry], 1, 1));
                }

                var results = await _searchService.AdvancedSearchAsync(
                    query.Expression, page, 10, cancellationToken).ConfigureAwait(false);
                var entries = results.Items.Select(MapSearchEntry).ToList().AsReadOnly();
                var stats = await _dashboardService.GetDashboardStatsAsync(cancellationToken).ConfigureAwait(false);
                var expectedCount = ResolveDashboardCount(stats, scope, template, creator, date, activity, entryId);
                return View(BuildDrillDownModel(
                    query, repo, [], entries, page, Math.Max(results.TotalCount, expectedCount)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Archive dashboard drill-down failed. Scope={Scope}; Template={Template}; Page={Page}.",
                    scope, template, page);
                return View(ArchiveViewModel.Error(
                    $"Could not load the requested Laserfiche results: {ex.Message}"));
            }
        }

        // 1. Resolve root entry ID from configuration (fast path — no API call)
        var rootId = _adapter.GetConfiguredRootEntryId();
        if (rootId <= 0)
        {
            try   { rootId = await _entryService.GetRootEntryIdAsync(cancellationToken); }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Archive/Index: failed to resolve root entry ID.");
                return View(ArchiveViewModel.Error(
                    "Could not connect to Laserfiche. Please check Settings."));
            }
        }

        if (entryId <= 0) entryId = rootId;

        // 2. Load current folder's display name (skip the extra call when at root)
        string currentName = "Repository";
        if (entryId != rootId)
        {
            try
            {
                var folderEntry = await _entryService.GetEntryAsync(entryId, cancellationToken);
                currentName = folderEntry.Name;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Archive/Index: could not load entry info for entryId={EntryId}.", entryId);
                currentName = $"Folder {entryId}";
            }
        }

        // 3. Parse breadcrumb from trail
        var breadcrumb = ParseTrail(trail, rootId);

        // 4. Load direct children — uses the confirmed folder-children endpoint (no OData params)
        IReadOnlyList<LFEntry> children;
        try
        {
            children = await _entryService.GetAllFolderChildrenAsync(entryId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Archive/Index: failed to load children for entryId={EntryId}.", entryId);
            return View(new ArchiveViewModel
            {
                CurrentEntryId = entryId,
                CurrentName    = currentName,
                Trail          = trail,
                Breadcrumb     = breadcrumb,
                Entries        = [],
                IsConnected    = true,
                ErrorMessage   =
                    "Could not load folder contents. " +
                    $"Laserfiche API error: {ex.Message}"
            });
        }

        // 5. Default sort: folders first, then documents, then alphabetically
        var sorted = children
            .OrderBy(e => e.EntryType == LFEntryType.Folder ? 0 : 1)
            .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
            .ToList()
            .AsReadOnly();

        return View(new ArchiveViewModel
        {
            CurrentEntryId = entryId,
            CurrentName    = currentName,
            Trail          = trail,
            Breadcrumb     = breadcrumb,
            Entries        = sorted,
            IsConnected    = true
        });
    }

    private static ArchiveViewModel BuildDrillDownModel(
        LaserficheArchiveQuery query,
        RepositoryDescriptor repository,
        IReadOnlyList<ArchiveTemplateResult> templates,
        IReadOnlyList<LFEntry> entries,
        int page,
        int totalCount)
    {
        var baseUrl = BuildWebClientBaseUrl(repository.ServerUrl, repository.RepositoryId);
        return new ArchiveViewModel
        {
            IsConnected = true,
            IsDrillDown = true,
            DrillDownScope = query.Scope,
            DrillDownTitle = query.Title,
            DrillDownDescription = query.Description,
            CurrentName = query.Title,
            Entries = entries,
            Templates = templates,
            OpenEntryId = query.OpenEntryId,
            PageNumber = page,
            PageSize = 10,
            TotalCount = totalCount,
            LaserficheWebClientUrl = query.IsTemplateCatalog
                ? baseUrl
                : $"{baseUrl}search={Uri.EscapeDataString(query.Expression)};view=search",
            LaserficheWebClientEntryUrlPrefix = $"{baseUrl}id="
        };
    }

    private static string BuildWebClientBaseUrl(string serverUrl, string repositoryId)
    {
        var server = new Uri(serverUrl, UriKind.Absolute);
        var origin = server.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return $"{origin}/Laserfiche/Browse.aspx?db={Uri.EscapeDataString(repositoryId)}#";
    }

    private static LFEntry MapSearchEntry(LFSearchResult item) => new()
    {
        Id = item.EntryId,
        Name = item.Name,
        FullPath = item.FullPath,
        EntryType = item.EntryType,
        TemplateId = item.TemplateId,
        TemplateName = item.TemplateName,
        Creator = item.Creator,
        CreationTime = item.CreationTime,
        LastModifiedTime = item.LastModifiedTime
    };

    public static int ResolveDashboardCount(
        DashboardStatsDto stats,
        string scope,
        string? template,
        string? creator,
        string? date,
        string? activity,
        int entryId)
    {
        if (!stats.IsConnected)
            return 0;

        return (scope ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "documents" => stats.TotalDocuments,
            "folders" => stats.TotalFolders,
            "with-template" => stats.DocsWithTemplate,
            "without-template" => stats.DocsWithoutTemplate,
            "template" => stats.TemplateStats.FirstOrDefault(item =>
                string.Equals(item.Name, template?.Trim(), StringComparison.OrdinalIgnoreCase))?.Count ?? 0,
            "root-folder" => stats.RootFolders.FirstOrDefault(item => item.EntryId == entryId)?.Documents ?? 0,
            "creator" => stats.UserDocumentActivity.FirstOrDefault(item =>
                string.Equals(item.Name, creator?.Trim(), StringComparison.OrdinalIgnoreCase))?.Created ?? 0,
            "activity" => ResolveActivityCount(stats, date, activity),
            "document" => entryId > 0 ? 1 : 0,
            _ => 0
        };
    }

    private static int ResolveActivityCount(DashboardStatsDto stats, string? date, string? activity)
    {
        if (!DateOnly.TryParseExact(date, "yyyy-MM-dd", out var day))
            return 0;
        var summary = stats.DocumentActivityByDay.FirstOrDefault(item => item.Date == day);
        return string.Equals(activity, "modified", StringComparison.OrdinalIgnoreCase)
            ? summary?.Modified ?? 0
            : summary?.Created ?? 0;
    }

    // GET /Archive/Detail?entryId=N
    // Returns a partial view loaded via fetch() for the document detail panel.
    public async Task<IActionResult> Detail(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId <= 0)
            return PartialView("_EntryDetail",
                ArchiveDetailViewModel.Error(entryId, "Invalid entry ID."));

        // ── 1. Load the entry ────────────────────────────────────────────────
        LFEntry entry;
        try
        {
            entry = await _entryService.GetEntryAsync(entryId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Archive/Detail: failed to load entry {EntryId}.", entryId);
            return PartialView("_EntryDetail",
                ArchiveDetailViewModel.Error(entryId, $"Could not load entry: {ex.Message}"));
        }

        _logger.LogInformation(
            "Archive metadata: EntryId={EntryId} Template=\"{Template}\" Type={Type}",
            entry.Id, entry.TemplateName ?? "(none)", entry.EntryType);

        // ── 2. Load entry field values ───────────────────────────────────────
        // Only attempt for documents that have a template applied.
        IReadOnlyList<LFFieldValue> rawFields = [];
        string? fieldsError = null;

        if (!string.IsNullOrWhiteSpace(entry.TemplateName))
        {
            try
            {
                rawFields = await _entryService.GetEntryFieldsAsync(entryId, cancellationToken);

                _logger.LogInformation(
                    "Archive metadata: EntryId={EntryId} EntryFields={Count} fieldDefinitionIds=[{Ids}]",
                    entryId, rawFields.Count,
                    string.Join(", ", rawFields.Select(f => f.FieldDefinitionId)));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Archive/Detail: entry fields endpoint failed for entry {EntryId}.", entryId);
                fieldsError =
                    $"Metadata field values could not be loaded from " +
                    $"GET /Repositories/{{repo}}/Entries/{entryId}/fields — " +
                    $"{ex.Message}";
            }
        }

        // ── 3. Load repository-wide field definitions (for name resolution) ─
        // Only needed when we have raw field values that may lack human-readable names.
        IReadOnlyDictionary<int, LFFieldDefinition> fieldDefs =
            new Dictionary<int, LFFieldDefinition>();
        string? fieldDefsError = null;

        if (rawFields.Count > 0)
        {
            try
            {
                fieldDefs = await _fieldDefService.GetFieldDefinitionsAsync(cancellationToken);

                _logger.LogInformation(
                    "Archive metadata: EntryId={EntryId} FieldDefinitions={Count}",
                    entryId, fieldDefs.Count);
            }
            catch (Exception ex)
            {
                // Non-fatal: if definitions fail we fall back to whatever name the
                // entry fields response already provided.
                _logger.LogWarning(ex,
                    "Archive/Detail: field definitions endpoint failed for entry {EntryId}. " +
                    "Will use names from entry fields response as fallback.", entryId);
                fieldDefsError = ex.Message;
            }
        }

        // ── 4. Join: resolve field name from definitions, keep inline name as fallback ─
        var resolvedFields = rawFields
            .Select(fv =>
            {
                // Prefer the name from the repository-wide FieldDefinitions if available.
                string resolvedName = fv.FieldName; // inline name from entry fields response

                if (fv.FieldDefinitionId > 0 &&
                    fieldDefs.TryGetValue(fv.FieldDefinitionId, out var def) &&
                    !string.IsNullOrWhiteSpace(def.Name))
                {
                    resolvedName = def.Name;
                }

                // Keep every field record returned by the Entry fields endpoint.
                // A missing definition/name is still an actual document field;
                // show its stable ID rather than silently dropping its value.
                if (string.IsNullOrWhiteSpace(resolvedName))
                {
                    resolvedName = fv.FieldDefinitionId > 0
                        ? $"Field {fv.FieldDefinitionId}"
                        : "Unnamed field";
                }

                return fv with { FieldName = resolvedName };
            })
            .ToList()
            .AsReadOnly();

        _logger.LogInformation(
            "Archive metadata: EntryId={EntryId} ResolvedFields={Count} " +
            "names=[{Names}]",
            entryId, resolvedFields.Count,
            string.Join(", ", resolvedFields.Select(f => f.FieldName)));

        // Compose the fields error message — surface the most helpful information.
        string? combinedFieldsError = fieldsError;
        if (combinedFieldsError is null && fieldDefsError is not null && rawFields.Count > 0)
        {
            // Fields loaded but definitions failed; names may be incomplete.
            combinedFieldsError =
                $"Field names may be incomplete — field definitions could not be loaded: {fieldDefsError}";
        }

        var preview = await LoadPreviewAsync(entry, cancellationToken).ConfigureAwait(false);

        return PartialView("_EntryDetail", new ArchiveDetailViewModel
        {
            Entry       = entry,
            Fields      = resolvedFields,
            FieldsError = combinedFieldsError,
            HasElectronicDocument = preview.HasElectronicDocument,
            ElectronicDocumentContentType = preview.ContentType,
            ElectronicDocumentExtension = preview.Extension,
            Pages = preview.Pages,
            PreviewError = preview.Error
        });
    }

    private async Task<ArchivePreviewResult> LoadPreviewAsync(
        LFEntry entry,
        CancellationToken cancellationToken)
    {
        if (entry.EntryType != LFEntryType.Document)
            return new ArchivePreviewResult();

        try
        {
            using var edoc = await _documentService
                .StreamEdocAsync(entry.Id, cancellationToken)
                .ConfigureAwait(false);

            return new ArchivePreviewResult
            {
                HasElectronicDocument = true,
                ContentType = edoc.ContentType,
                Extension = edoc.Extension
            };
        }
        catch (LFPortal.Domain.Exceptions.LaserficheException ex)
            when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            // A valid Laserfiche document may have image pages but no electronic file.
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Archive preview: electronic document check failed for entry {EntryId}.", entry.Id);
        }

        try
        {
            var pages = await _documentService
                .GetDocumentPagesAsync(entry.Id, cancellationToken)
                .ConfigureAwait(false);

            if (pages.Count == 0 && entry.PageCount is > 0)
                pages = BuildPageFallback(entry.PageCount.Value);

            return new ArchivePreviewResult { Pages = pages };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Archive preview: page list failed for entry {EntryId}.", entry.Id);

            if (entry.PageCount is > 0)
                return new ArchivePreviewResult { Pages = BuildPageFallback(entry.PageCount.Value) };

            return new ArchivePreviewResult
            {
                Error = "Document preview is not available from Laserfiche at this time."
            };
        }
    }

    private static IReadOnlyList<LFDocumentPage> BuildPageFallback(int pageCount) =>
        Enumerable.Range(1, pageCount)
            .Select(number => new LFDocumentPage { PageNumber = number })
            .ToList()
            .AsReadOnly();

    private sealed record ArchivePreviewResult
    {
        public bool HasElectronicDocument { get; init; }
        public string? ContentType { get; init; }
        public string? Extension { get; init; }
        public IReadOnlyList<LFDocumentPage> Pages { get; init; } = [];
        public string? Error { get; init; }
    }

    // ── Breadcrumb parser ─────────────────────────────────────────────────────

    /// <summary>
    /// Parses the <paramref name="trail"/> query parameter into a breadcrumb list.
    /// Trail format: pipe-separated segments, each <c>id:Name</c> (name is URI-encoded).
    /// Root is always prepended automatically.
    /// </summary>
    private static IReadOnlyList<BreadcrumbItem> ParseTrail(string trail, int rootId)
    {
        // Root is always the first breadcrumb item with an empty trail
        var items = new List<BreadcrumbItem>
        {
            new() { EntryId = rootId, Name = "Repository", Trail = "" }
        };

        if (string.IsNullOrWhiteSpace(trail))
            return items.AsReadOnly();

        var segments = trail.Split('|', StringSplitOptions.RemoveEmptyEntries);

        for (var i = 0; i < segments.Length; i++)
        {
            var seg   = segments[i];
            var colon = seg.IndexOf(':');
            if (colon <= 0) continue;
            if (!int.TryParse(seg[..colon], out var id)) continue;

            var name = Uri.UnescapeDataString(seg[(colon + 1)..]);

            // Skip duplicating the root entry (trail may include it)
            if (id == rootId) continue;

            // The trail for this item's link = all segments that came before it
            var linkTrail = string.Join("|", segments[..i]);

            items.Add(new BreadcrumbItem
            {
                EntryId = id,
                Name    = name,
                Trail   = linkTrail
            });
        }

        return items.AsReadOnly();
    }
}
