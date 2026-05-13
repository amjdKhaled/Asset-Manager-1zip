using ArabicPdfExtraction.Api.Contracts;
using ArabicPdfExtraction.Api.Models;

namespace ArabicPdfExtraction.Api.Services;

public sealed class TempFileStore : ITempFileStore
{
    private readonly string _baseDir;

    public TempFileStore(IConfiguration config)
    {
        _baseDir = config["Storage:TempDir"] ?? Path.Combine(AppContext.BaseDirectory, "tmp");
        Directory.CreateDirectory(_baseDir);
    }

    public async Task<PdfUploadResponse> SaveAsync(IFormFile file, CancellationToken cancellationToken)
    {
        var uploadId = Guid.NewGuid().ToString("N");
        var safeName = Path.GetFileName(file.FileName);
        var target = Path.Combine(_baseDir, $"{uploadId}_{safeName}");

        await using var fs = File.Create(target);
        await file.CopyToAsync(fs, cancellationToken);

        return new PdfUploadResponse { UploadId = uploadId, FileName = safeName, TempPath = target };
    }

    public string ResolvePath(string uploadId)
    {
        var found = Directory.EnumerateFiles(_baseDir, $"{uploadId}_*.pdf").FirstOrDefault();
        if (found is null) throw new FileNotFoundException("Upload ID not found");
        return found;
    }
}
