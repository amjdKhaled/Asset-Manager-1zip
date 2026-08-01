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