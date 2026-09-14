using LFPortal.Domain.Entities;

namespace LFPortal.Web.Controllers;

// ── Archive browser view models ───────────────────────────────────────────────

/// <summary>One segment in the folder breadcrumb trail.</summary>
public sealed record BreadcrumbItem
{
    /// <summary>Laserfiche Entry ID of this folder.</summary>
    public int EntryId { get; init; }

    /// <summary>Display name of this folder.</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The trail query-string value to pass when navigating to this breadcrumb item.
    /// Empty string = navigate to root.
    /// </summary>
    public string Trail { get; init; } = string.Empty;
}

/// <summary>View model for Archive/Index.</summary>
public sealed class ArchiveViewModel
{
    /// <summary>True when opened from a dashboard statistic rather than a folder.</summary>
    public bool IsDrillDown { get; init; }

    public string DrillDownTitle { get; init; } = string.Empty;
    public string DrillDownDescription { get; init; } = string.Empty;
    public string DrillDownScope { get; init; } = string.Empty;
    public int OpenEntryId { get; init; }

    /// <summary>Template catalog rows used by the Total Templates drill-down.</summary>
    public IReadOnlyList<ArchiveTemplateResult> Templates { get; init; } = [];

    /// <summary>Entry ID of the folder currently being browsed.</summary>
    public int CurrentEntryId { get; init; }

    /// <summary>Display name of the current folder.</summary>
    public string CurrentName { get; init; } = "Repository";

    /// <summary>Raw trail string (opaque to the view — used to build child folder links).</summary>
    public string Trail { get; init; } = string.Empty;

    /// <summary>Breadcrumb path from root to (and including) the current folder.</summary>
    public IReadOnlyList<BreadcrumbItem> Breadcrumb { get; init; } = [];

    /// <summary>Direct children of the current folder, folders-first then alphabetical.</summary>
    public IReadOnlyList<LFEntry> Entries { get; init; } = [];

    /// <summary>Non-null when an error occurred loading the folder.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Whether the Laserfiche connection is available.</summary>
    public bool IsConnected { get; init; }

    public static ArchiveViewModel Error(string message) => new()
    {
        IsConnected  = false,
        ErrorMessage = message
    };
}

/// <summary>View model for Archive/_EntryDetail partial.</summary>
public sealed class ArchiveDetailViewModel
{
    public LFEntry? Entry { get; init; }

    /// <summary>Metadata field values; empty if none or if endpoint is unavailable.</summary>
    public IReadOnlyList<LFFieldValue> Fields { get; init; } = [];

    /// <summary>Non-null when the fields endpoint returned an error.</summary>
    public string? FieldsError { get; init; }

    /// <summary>Non-null when the entry itself could not be loaded.</summary>
    public string? EntryError { get; init; }

    public bool HasElectronicDocument { get; init; }
    public string? ElectronicDocumentContentType { get; init; }
    public string? ElectronicDocumentExtension { get; init; }
    public IReadOnlyList<LFDocumentPage> Pages { get; init; } = [];
    public string? PreviewError { get; init; }

    public bool IsInlineElectronicDocument =>
        ElectronicDocumentContentType is "application/pdf"
            or "image/png"
            or "image/jpeg"
            or "image/webp"
            or "image/gif"
            or "image/bmp";

    public static ArchiveDetailViewModel Error(int entryId, string message) => new()
    {
        Entry      = new LFEntry { Id = entryId, Name = $"Entry {entryId}" },
        EntryError = message
    };
}
