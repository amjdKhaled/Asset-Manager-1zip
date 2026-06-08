using ArabicPdfExtraction.Api.Contracts;
using PdfiumViewer;
using System.Drawing.Imaging;

namespace ArabicPdfExtraction.Api.Services;

public sealed class PdfImageRendererService : IPdfImageRendererService
{
    public Task<byte[]> RenderPageAsPngAsync(string pdfPath, int pageNumber, CancellationToken cancellationToken)
    {
        using var document = PdfDocument.Load(pdfPath);
        using var image = document.Render(pageNumber - 1, 300, 300, true);
        using var ms = new MemoryStream();
        image.Save(ms, ImageFormat.Png);
        return Task.FromResult(ms.ToArray());
    }
}
