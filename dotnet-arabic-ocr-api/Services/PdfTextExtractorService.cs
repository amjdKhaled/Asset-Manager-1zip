using ArabicPdfExtraction.Api.Contracts;
using UglyToad.PdfPig;

namespace ArabicPdfExtraction.Api.Services;

public sealed class PdfTextExtractorService : IPdfTextExtractorService
{
    public Task<int> GetPageCountAsync(string pdfPath, CancellationToken cancellationToken)
    {
        using var doc = PdfDocument.Open(pdfPath);
        return Task.FromResult(doc.NumberOfPages);
    }

    public Task<string> ExtractPageTextAsync(string pdfPath, int pageNumber, CancellationToken cancellationToken)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var page = doc.GetPage(pageNumber);
        return Task.FromResult(page.Text ?? string.Empty);
    }

    public Task<Dictionary<string, string>> ExtractMetadataAsync(string pdfPath, CancellationToken cancellationToken)
    {
        using var doc = PdfDocument.Open(pdfPath);
        var meta = new Dictionary<string, string>
        {
            ["Title"] = doc.Information.Title ?? string.Empty,
            ["Author"] = doc.Information.Author ?? string.Empty,
            ["Producer"] = doc.Information.Producer ?? string.Empty,
            ["CreationDate"] = doc.Information.CreationDate?.ToString("O") ?? string.Empty
        };
        return Task.FromResult(meta);
    }
}
