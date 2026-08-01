using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Infrastructure.Adapters;
using Microsoft.AspNetCore.Mvc;

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
    private readonly ILaserficheApiAdapter              _adapter;
    private readonly ILogger<ArchiveController>         _logger;

    public ArchiveController(
        ILaserficheEntryService           entryService,
        ILaserficheFieldDefinitionService fieldDefService,
        ILaserficheApiAdapter             adapter,
        ILogger<ArchiveController>        logger)
    {
        _entryService    = entryService;
        _fieldDefService = fieldDefService;
        _adapter         = adapter;
        _logger          = logger;
    }

    // GET /Archive          → root
    // GET /Archive?entryId=N&trail=...  → specific folder
    public async Task<IActionResult> Index(
        int    entryId = 0,
        string trail   = "",
        CancellationToken cancellationToken = default)
    {
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

        return PartialView("_EntryDetail", new ArchiveDetailViewModel
        {
            Entry       = entry,
            Fields      = resolvedFields,
            FieldsError = combinedFieldsError
        });
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
