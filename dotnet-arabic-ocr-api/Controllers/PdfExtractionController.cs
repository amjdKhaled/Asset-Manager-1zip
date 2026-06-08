using ArabicPdfExtraction.Api.Contracts;
using ArabicPdfExtraction.Api.Models;
using ArabicPdfExtraction.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace ArabicPdfExtraction.Api.Controllers;

[ApiController]
[Route("api/pdf")]
public sealed class PdfExtractionController : ControllerBase
{
    [HttpPost("upload")]
    [RequestSizeLimit(50_000_000)]
    public async Task<ActionResult<PdfUploadResponse>> Upload([FromForm] IFormFile file, [FromServices] ITempFileStore tempStore, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return BadRequest(new { success = false, error = "File is required." });
        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) return BadRequest(new { success = false, error = "Only PDF is supported." });
        var result = await tempStore.SaveAsync(file, ct);
        return Ok(result);
    }

    [HttpPost("extract")]
    public async Task<ActionResult<PdfExtractionResult>> Extract([FromBody] ExtractTextRequest request, [FromServices] ITempFileStore tempStore, [FromServices] PdfExtractionOrchestrator orchestrator, CancellationToken ct)
    {
        try
        {
            var path = tempStore.ResolvePath(request.UploadId);
            var result = await orchestrator.ExtractAsync(path, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new PdfExtractionResult { Success = false, Error = ex.Message });
        }
    }
}
