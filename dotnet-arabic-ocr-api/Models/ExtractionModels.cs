namespace ArabicPdfExtraction.Api.Models;

public sealed class PdfUploadResponse
{
    public required string UploadId { get; init; }
    public required string FileName { get; init; }
    public required string TempPath { get; init; }
}

public sealed class ExtractTextRequest
{
    public required string UploadId { get; init; }
}

public sealed class PageExtractionResult
{
    public int PageNumber { get; init; }
    public bool UsedOcr { get; init; }
    public string Text { get; init; } = string.Empty;
    public float? OcrConfidence { get; init; }
}

public sealed class PdfExtractionResult
{
    public bool Success { get; init; }
    public string? Error { get; init; }
    public string CombinedText { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = new();
    public List<PageExtractionResult> Pages { get; init; } = new();
}
