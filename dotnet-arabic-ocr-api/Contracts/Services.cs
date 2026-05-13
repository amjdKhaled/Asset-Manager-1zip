using ArabicPdfExtraction.Api.Models;

namespace ArabicPdfExtraction.Api.Contracts;

public interface ITempFileStore
{
    Task<PdfUploadResponse> SaveAsync(IFormFile file, CancellationToken cancellationToken);
    string ResolvePath(string uploadId);
}

public interface IPdfTextExtractorService
{
    Task<string> ExtractPageTextAsync(string pdfPath, int pageNumber, CancellationToken cancellationToken);
    Task<int> GetPageCountAsync(string pdfPath, CancellationToken cancellationToken);
    Task<Dictionary<string, string>> ExtractMetadataAsync(string pdfPath, CancellationToken cancellationToken);
}

public interface IPdfImageRendererService
{
    Task<byte[]> RenderPageAsPngAsync(string pdfPath, int pageNumber, CancellationToken cancellationToken);
}

public interface IArabicOcrService
{
    Task<(string text, float confidence)> ExtractTextAsync(byte[] imageBytes, CancellationToken cancellationToken);
}

public interface ITextCleanupService
{
    string Clean(string text);
}
