using ArabicPdfExtraction.Api.Contracts;
using ArabicPdfExtraction.Api.Models;

namespace ArabicPdfExtraction.Api.Services;

public sealed class PdfExtractionOrchestrator
{
    private readonly IPdfTextExtractorService _pdfText;
    private readonly IPdfImageRendererService _renderer;
    private readonly IArabicOcrService _ocr;
    private readonly ITextCleanupService _cleanup;

    public PdfExtractionOrchestrator(IPdfTextExtractorService pdfText, IPdfImageRendererService renderer, IArabicOcrService ocr, ITextCleanupService cleanup)
    {
        _pdfText = pdfText;
        _renderer = renderer;
        _ocr = ocr;
        _cleanup = cleanup;
    }

    public async Task<PdfExtractionResult> ExtractAsync(string pdfPath, CancellationToken ct)
    {
        var pages = new List<PageExtractionResult>();
        var pageCount = await _pdfText.GetPageCountAsync(pdfPath, ct);
        var metadata = await _pdfText.ExtractMetadataAsync(pdfPath, ct);

        for (var i = 1; i <= pageCount; i++)
        {
            ct.ThrowIfCancellationRequested();
            var text = _cleanup.Clean(await _pdfText.ExtractPageTextAsync(pdfPath, i, ct));
            if (!string.IsNullOrWhiteSpace(text))
            {
                pages.Add(new PageExtractionResult { PageNumber = i, Text = text, UsedOcr = false });
                continue;
            }

            var png = await _renderer.RenderPageAsPngAsync(pdfPath, i, ct);
            var (ocrText, confidence) = await _ocr.ExtractTextAsync(png, ct);
            pages.Add(new PageExtractionResult
            {
                PageNumber = i,
                UsedOcr = true,
                Text = _cleanup.Clean(ocrText),
                OcrConfidence = confidence,
            });
        }

        var combined = string.Join("\n\n", pages.OrderBy(p => p.PageNumber).Select(p => p.Text));
        return new PdfExtractionResult { Success = true, CombinedText = combined, Pages = pages, Metadata = metadata };
    }
}
