using ArabicPdfExtraction.Api.Contracts;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Tesseract;

namespace ArabicPdfExtraction.Api.Services;

public sealed class ArabicOcrService : IArabicOcrService
{
    private readonly string _tessDataPath;

    public ArabicOcrService(IConfiguration config)
    {
        _tessDataPath = config["Ocr:TessDataPath"] ?? Path.Combine(AppContext.BaseDirectory, "tessdata");
    }

    public async Task<(string text, float confidence)> ExtractTextAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        var preprocessed = await PreprocessAsync(imageBytes, cancellationToken);
        using var engine = new TesseractEngine(_tessDataPath, "ara+eng", EngineMode.LstmOnly);
        engine.DefaultPageSegMode = PageSegMode.Auto;
        using var pix = Pix.LoadFromMemory(preprocessed);
        using var page = engine.Process(pix);
        return (page.GetText() ?? string.Empty, page.GetMeanConfidence());
    }

    private static async Task<byte[]> PreprocessAsync(byte[] imageBytes, CancellationToken cancellationToken)
    {
        using var image = Image.Load<Rgba32>(imageBytes);
        image.Mutate(x => x.Grayscale().BinaryThreshold(0.55f).AutoOrient());
        await using var ms = new MemoryStream();
        await image.SaveAsync(ms, new PngEncoder(), cancellationToken);
        return ms.ToArray();
    }
}
