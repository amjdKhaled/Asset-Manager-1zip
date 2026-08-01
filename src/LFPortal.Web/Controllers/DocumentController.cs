using System.Net;
using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Domain.Entities;
using LFPortal.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace LFPortal.Web.Controllers;

/// <summary>
/// Proxies confirmed electronic-document content through LFPortal. Laserfiche
/// credentials and bearer tokens remain entirely on the server.
/// </summary>
public sealed class DocumentController : Controller
{
    private readonly ILaserficheDocumentService _documentService;
    private readonly ILaserficheEntryService _entryService;
    private readonly ILaserficheFieldDefinitionService _fieldDefinitionService;
    private readonly ILogger<DocumentController> _logger;

    public DocumentController(
        ILaserficheDocumentService documentService,
        ILaserficheEntryService entryService,
        ILaserficheFieldDefinitionService fieldDefinitionService,
        ILogger<DocumentController> logger)
    {
        _documentService = documentService;
        _entryService = entryService;
        _fieldDefinitionService = fieldDefinitionService;
        _logger = logger;
    }

    // GET /Document/View/{entryId}
    public async Task<IActionResult> View(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        if (entryId <= 0)
            return View("View", DocumentViewModel.Error("The document ID is invalid."));

        LFEntry entry;
        try
        {
            entry = await _documentService
                .GetDocumentMetadataAsync(entryId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document/View: failed to load entry {EntryId}.", entryId);
            return View("View", DocumentViewModel.Error(UserFacingError(ex, "load the document")));
        }

        if (entry.EntryType != LFEntryType.Document)
            return View("View", DocumentViewModel.Error("Only document entries can be opened in the document viewer."));

        var fields = await LoadFieldsAsync(entryId, cancellationToken).ConfigureAwait(false);
        var path = entry.FullPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            try
            {
                path = await _entryService.GetEntryPathAsync(entryId, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Document/View: path unavailable for entry {EntryId}.", entryId);
            }
        }

        var model = new DocumentViewModel
        {
            Entry = entry,
            Fields = fields.Values,
            FieldsError = fields.Error,
            Path = path,
            HasElectronicDocument = false
        };

        // A 404 here means this valid entry has no edoc; it does not mean that
        // the entry itself is missing. Other statuses remain visible as a
        // connection/authentication state rather than an exception page.
        try
        {
            using var edoc = await _documentService
                .StreamEdocAsync(entryId, cancellationToken)
                .ConfigureAwait(false);

            model = model with
            {
                HasElectronicDocument = true,
                ElectronicDocumentContentType = edoc.ContentType,
                ElectronicDocumentFileName = edoc.FileName,
                ElectronicDocumentExtension = edoc.Extension
            };
        }
        catch (LaserficheException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            _logger.LogInformation("Document/View: entry {EntryId} has no electronic document.", entryId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document/View: failed to inspect edoc for entry {EntryId}.", entryId);
            return View("View", model with
            {
                ErrorMessage = UserFacingError(ex, "check electronic-document availability")
            });
        }

        // When no electronic file is present, attempt to load Laserfiche image pages
        // so the viewer can render them via the server-side PageImage proxy.
        if (!model.HasElectronicDocument && model.HasLaserfichePages)
        {
            try
            {
                var pages = await _documentService
                    .GetDocumentPagesAsync(entryId, cancellationToken)
                    .ConfigureAwait(false);

                model = model with { Pages = pages };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Document/View: page list unavailable for entry {EntryId}.", entryId);
            }
        }

        return View("View", model);
    }

    // GET /Document/Content/{entryId}
    public async Task<IActionResult> Content(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        LFEntry? entry;
        try
        {
            entry = await GetDocumentEntryOrNullAsync(entryId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document/Content: entry lookup failed for {EntryId}.", entryId);
            return ProxyError(ex, "retrieve the document");
        }

        if (entry is null)
            return NotFound("The requested document was not found.");

        LaserficheEdocStream? edoc = null;
        try
        {
            edoc = await _documentService.StreamEdocAsync(entryId, cancellationToken).ConfigureAwait(false);
            if (!IsInlineType(edoc.ContentType))
            {
                edoc.Dispose();
                return StatusCode(StatusCodes.Status415UnsupportedMediaType,
                    "This document cannot be previewed directly in the browser.");
            }

            Response.Headers.ContentDisposition = "inline";
            return File(edoc.Content, edoc.ContentType, enableRangeProcessing: false);
        }
        catch (LaserficheException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            edoc?.Dispose();
            return NotFound("This document does not have an electronic file.");
        }
        catch (Exception ex)
        {
            edoc?.Dispose();
            _logger.LogError(ex, "Document/Content: failed for entry {EntryId}.", entryId);
            return ProxyError(ex, "retrieve the electronic document");
        }
    }

    // GET /Document/PageImage/{entryId}/{pageNumber}
    public async Task<IActionResult> PageImage(
        int entryId,
        int pageNumber,
        CancellationToken cancellationToken = default)
    {
        if (entryId <= 0 || pageNumber <= 0)
            return BadRequest("Invalid entry ID or page number.");

        LaserficheEdocStream? image = null;
        try
        {
            image = await _documentService
                .GetPageImageAsync(entryId, pageNumber, cancellationToken)
                .ConfigureAwait(false);

            Response.Headers.CacheControl = "private, max-age=300";
            return File(image.Content, image.ContentType, enableRangeProcessing: false);
        }
        catch (LaserficheException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            image?.Dispose();
            return NotFound("Page image not found.");
        }
        catch (Exception ex)
        {
            image?.Dispose();
            _logger.LogError(ex,
                "Document/PageImage: failed for entry {EntryId} page {PageNumber}.",
                entryId, pageNumber);
            return ProxyError(ex, "retrieve the page image");
        }
    }

    // GET /Document/Download/{entryId}
    public async Task<IActionResult> Download(
        int entryId,
        CancellationToken cancellationToken = default)
    {
        LFEntry? entry;
        try
        {
            entry = await GetDocumentEntryOrNullAsync(entryId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Document/Download: entry lookup failed for {EntryId}.", entryId);
            return ProxyError(ex, "retrieve the document");
        }

        if (entry is null)
            return NotFound("The requested document was not found.");

        LaserficheEdocStream? edoc = null;
        try
        {
            edoc = await _documentService.StreamEdocAsync(entryId, cancellationToken).ConfigureAwait(false);
            var fileName = edoc.FileName;
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = entry.Name + (edoc.Extension ?? string.Empty);

            return File(edoc.Content, edoc.ContentType, fileName, enableRangeProcessing: false);
        }
        catch (LaserficheException ex) when (ex.StatusCode == (int)HttpStatusCode.NotFound)
        {
            edoc?.Dispose();
            return NotFound("This document does not have an electronic file.");
        }
        catch (Exception ex)
        {
            edoc?.Dispose();
            _logger.LogError(ex, "Document/Download: failed for entry {EntryId}.", entryId);
            return ProxyError(ex, "retrieve the electronic document");
        }
    }

    private async Task<LFEntry?> GetDocumentEntryOrNullAsync(
        int entryId,
        CancellationToken cancellationToken)
    {
        if (entryId <= 0) return null;

        var entry = await _entryService.GetEntryAsync(entryId, cancellationToken).ConfigureAwait(false);
        return entry.EntryType == LFEntryType.Document ? entry : null;
    }

    private async Task<(IReadOnlyList<LFFieldValue> Values, string? Error)> LoadFieldsAsync(
        int entryId,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<LFFieldValue> rawFields;
        try
        {
            rawFields = await _entryService.GetEntryFieldsAsync(entryId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document/View: fields unavailable for entry {EntryId}.", entryId);
            return ([], "Metadata fields could not be loaded from Laserfiche.");
        }

        if (rawFields.Count == 0) return ([], null);

        IReadOnlyDictionary<int, LFFieldDefinition> definitions;
        try
        {
            definitions = await _fieldDefinitionService
                .GetFieldDefinitionsAsync(cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Document/View: field definitions unavailable for entry {EntryId}.", entryId);
            return (ResolveNames(rawFields, new Dictionary<int, LFFieldDefinition>()),
                "Some metadata field names may be unavailable.");
        }

        return (ResolveNames(rawFields, definitions), null);
    }

    private static IReadOnlyList<LFFieldValue> ResolveNames(
        IReadOnlyList<LFFieldValue> fields,
        IReadOnlyDictionary<int, LFFieldDefinition> definitions) =>
        fields.Select(field =>
        {
            if (field.FieldDefinitionId > 0 &&
                definitions.TryGetValue(field.FieldDefinitionId, out var definition) &&
                !string.IsNullOrWhiteSpace(definition.Name))
            {
                return field with { FieldName = definition.Name };
            }

            return string.IsNullOrWhiteSpace(field.FieldName)
                ? field with
                {
                    FieldName = field.FieldDefinitionId > 0
                        ? $"Field {field.FieldDefinitionId}"
                        : "Unnamed field"
                }
                : field;
        }).ToList().AsReadOnly();

    private static bool IsInlineType(string contentType) =>
        contentType.Equals("application/pdf", StringComparison.OrdinalIgnoreCase) ||
        contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) &&
        contentType is not "image/svg+xml";

    private IActionResult ProxyError(Exception exception, string operation)
    {
        if (exception is LaserficheException lf &&
            lf.StatusCode is 401 or 403)
        {
            return StatusCode(
                StatusCodes.Status502BadGateway,
                "Laserfiche authentication has expired or is not authorised. Please check Settings.");
        }

        return StatusCode(
            StatusCodes.Status502BadGateway,
            $"LFPortal could not {operation} from Laserfiche. Please check the connection in Settings.");
    }

    private static string UserFacingError(Exception exception, string operation)
    {
        if (exception is LaserficheException lf)
        {
            return lf.StatusCode switch
            {
                401 or 403 => "Laserfiche authentication has expired or is not authorised. Please check Settings.",
                404 => "The requested document was not found in Laserfiche.",
                _ => $"LFPortal could not {operation} from Laserfiche (HTTP {lf.StatusCode})."
            };
        }

        return $"LFPortal could not {operation} from Laserfiche. Please check the connection in Settings.";
    }
}