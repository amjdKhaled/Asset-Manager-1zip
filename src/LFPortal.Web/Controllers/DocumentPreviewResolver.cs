using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;

namespace LFPortal.Web.Controllers;

/// <summary>Electronic files and image pages are independent document components.</summary>
public static class DocumentPreviewResolver
{
    public static async Task<DocumentViewModel> ResolveAsync(
        ILaserficheDocumentService service, LFEntry entry,
        ILogger logger, CancellationToken cancellationToken)
    {
        var model = new DocumentViewModel { Entry = entry };
        Exception? edocError = null;
        try
        {
            using var edoc = await service.StreamEdocAsync(entry.Id, cancellationToken);
            if (edoc.ContentLength != 0)
                model = model with
                {
                    HasElectronicDocument = true,
                    ElectronicDocumentContentType = edoc.ContentType,
                    ElectronicDocumentFileName = edoc.FileName,
                    ElectronicDocumentExtension = edoc.Extension
                };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            edocError = ex;
            logger.LogWarning(ex, "Electronic preview unavailable for entry {EntryId}; checking image pages.", entry.Id);
        }

        if (model.IsInlineElectronicDocument) return model;
        // An authentication failure must not be presented as an empty document.
        if (edocError is LaserficheException { StatusCode: 401 or 403 })
            return model with { PreviewError = "تعذر عرض الوثيقة. تحقق من تسجيل الدخول وصلاحية عرض المستند." };

        Exception? pagesError = null;
        try
        {
            var pages = await service.GetDocumentPagesAsync(entry.Id, cancellationToken);
            model = model with { Pages = pages };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            pagesError = ex;
            logger.LogWarning(ex, "Image page list unavailable for entry {EntryId}.", entry.Id);
        }
        if (model.Pages.Count == 0 && pagesError is not null && entry.PageCount is > 0)
            model = model with
            {
                Pages = Enumerable.Range(1, entry.PageCount.Value)
                    .Select(number => new LFDocumentPage { PageNumber = number }).ToArray()
            };
        if (model.Pages.Count > 0 || model.HasElectronicDocument) return model;

        // A 400 from edoc alone does not prove that the document is empty.
        // Require an empty page list or explicit zero-page metadata as well.
        var absentEdoc = edocError is null or LaserficheException { StatusCode: 404 } ||
            (edocError is LaserficheException { StatusCode: 400 } && entry.FileSizeBytes is null or 0);
        var absentPages = pagesError is null ||
            (entry.PageCount == 0 && pagesError is LaserficheException { StatusCode: 400 or 404 or 405 });
        return absentEdoc && absentPages ? model : model with
        {
            PreviewError = "تعذر تحميل معاينة الوثيقة من Laserfiche. أعد المحاولة أو افتح الوثيقة في Laserfiche."
        };
    }
}
