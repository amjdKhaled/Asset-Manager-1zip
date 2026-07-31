using LFPortal.Domain.Entities;

namespace LFPortal.Application.Interfaces;

/// <summary>
/// Provides document retrieval operations: page metadata, electronic document streaming,
/// and page image retrieval. All content is streamed directly from Laserfiche —
/// no temporary files are written to disk on the portal server.
/// </summary>
public interface ILaserficheDocumentService
{
    /// <summary>
    /// Retrieves metadata about each page of the specified document.
    /// Returns an empty list for entries that are not documents or have no pages.
    /// </summary>
    /// <param name="entryId">Laserfiche Entry ID of the document.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<IReadOnlyList<LFDocumentPage>> GetDocumentPagesAsync(
        int entryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams the electronic document (edoc) for the specified entry directly into
    /// <paramref name="destination"/> without buffering the entire file in memory.
    /// Callers are responsible for setting appropriate response headers before calling.
    /// </summary>
    /// <param name="entryId">Laserfiche Entry ID of the document.</param>
    /// <param name="destination">Target stream. Typically the HTTP response body stream.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task StreamEdocAsync(
        int entryId,
        Stream destination,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the rendered image for a single page as a readable stream.
    /// The caller is responsible for disposing the returned stream.
    /// </summary>
    /// <param name="entryId">Laserfiche Entry ID of the document.</param>
    /// <param name="pageNumber">1-based page number.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<Stream> GetPageImageAsync(
        int entryId,
        int pageNumber,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the entry metadata for a document, including file size and page count.
    /// Equivalent to <see cref="ILaserficheEntryService.GetEntryAsync"/> but scoped
    /// to document-type entries for semantic clarity.
    /// </summary>
    Task<LFEntry> GetDocumentMetadataAsync(
        int entryId,
        CancellationToken cancellationToken = default);
}
