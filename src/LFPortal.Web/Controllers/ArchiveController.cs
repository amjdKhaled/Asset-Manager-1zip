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
    private readonly ILaserficheEntryService _entryService;
    private readonly ILaserficheApiAdapter   _adapter;
    private readonly ILogger<ArchiveController> _logger;

    public ArchiveController(
        ILaserficheEntryService     entryService,
        ILaserficheApiAdapter       adapter,
        ILogger<ArchiveController>  logger)
    {
        _entryService = entryService;
        _adapter      = adapter;
        _logger       = logger;
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

        // Load the entry
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

        // Attempt metadata fields — only for documents with a template applied.
        // Endpoint: GET /Repositories/{repo}/Entries/{id}/fields
        // This endpoint has not been tested on the live server; errors are caught and reported.
        IReadOnlyList<LFFieldValue> fields = [];
        string? fieldsError = null;

        if (entry.EntryType == LFEntryType.Document &&
            !string.IsNullOrWhiteSpace(entry.TemplateName))
        {
            try
            {
                fields = await _entryService.GetEntryFieldsAsync(entryId, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Archive/Detail: fields endpoint failed for entry {EntryId}.", entryId);
                fieldsError =
                    $"Metadata fields could not be loaded. " +
                    $"Required Laserfiche API endpoint: " +
                    $"GET /Repositories/{{repo}}/Entries/{entryId}/fields — " +
                    $"confirm availability in Swagger if fields are needed.";
            }
        }

        return PartialView("_EntryDetail", new ArchiveDetailViewModel
        {
            Entry       = entry,
            Fields      = fields,
            FieldsError = fieldsError
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
