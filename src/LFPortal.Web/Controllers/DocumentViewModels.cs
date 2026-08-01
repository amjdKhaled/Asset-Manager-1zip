using LFPortal.Domain.Entities;

namespace LFPortal.Web.Controllers;

/// <summary>View model for the confirmed electronic-document viewer.</summary>
public sealed record DocumentViewModel
{
    public LFEntry? Entry { get; init; }
    public IReadOnlyList<LFFieldValue> Fields { get; init; } = [];
    public string? FieldsError { get; init; }
    public string? Path { get; init; }
    public bool HasElectronicDocument { get; init; }
    public string? ElectronicDocumentContentType { get; init; }
    public string? ElectronicDocumentFileName { get; init; }
    public string? ElectronicDocumentExtension { get; init; }

    /// <summary>
    /// Page metadata returned by the Laserfiche pages endpoint. Populated only when
    /// the document has Laserfiche image pages and no electronic file.
    /// </summary>
    public IReadOnlyList<LFDocumentPage> Pages { get; init; } = [];

    /// <summary>
    /// True when the entry reports a positive page count, regardless of whether
    /// the pages list has been loaded yet.
    /// </summary>
    public bool HasLaserfichePages => Entry?.PageCount is > 0;

    public string? ErrorMessage { get; init; }

    public bool IsInlineElectronicDocument =>
        ElectronicDocumentContentType is "application/pdf"
            or "image/png"
            or "image/jpeg"
            or "image/webp";

    public static DocumentViewModel Error(string message) => new()
    {
        ErrorMessage = message
    };
}