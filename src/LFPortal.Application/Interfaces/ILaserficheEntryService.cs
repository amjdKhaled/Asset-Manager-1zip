using LFPortal.Domain.Common;
using LFPortal.Domain.Entities;

namespace LFPortal.Application.Interfaces;

/// <summary>
/// Provides entry-level operations: retrieving individual entries, their metadata fields,
/// templates, hierarchy paths, and folder contents. All data comes directly from
/// the Laserfiche Repository API — nothing is sourced from local state.
/// </summary>
public interface ILaserficheEntryService
{
    /// <summary>
    /// Retrieves a single entry by its numeric Laserfiche Entry ID.
    /// </summary>
    /// <param name="entryId">The Laserfiche Entry ID.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<LFEntry> GetEntryAsync(int entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves all metadata field values applied to the specified entry.
    /// Returns an empty list for entries with no template or no field values.
    /// </summary>
    Task<IReadOnlyList<LFFieldValue>> GetEntryFieldsAsync(
        int entryId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the template and its field definitions applied to the specified entry.
    /// Returns <c>null</c> if no template is applied.
    /// </summary>
    Task<LFTemplate?> GetEntryTemplateAsync(int entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves the full repository path string for the specified entry,
    /// e.g. <c>\Department\Year\DocumentName</c>.
    /// </summary>
    Task<string> GetEntryPathAsync(int entryId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a paged list of the direct children of the specified folder entry.
    /// </summary>
    /// <param name="entryId">Entry ID of the parent folder.</param>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Number of entries per page (max 100).</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<PagedResult<LFEntry>> GetEntryChildrenAsync(
        int entryId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a flat list of folder entries up to the specified depth below
    /// <paramref name="rootEntryId"/>. Used to build the folder tree in the Archive page.
    /// </summary>
    /// <param name="rootEntryId">Entry ID of the root folder to start from (typically 1).</param>
    /// <param name="depth">Maximum folder depth to traverse. Must be between 1 and 5.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<IReadOnlyList<LFEntry>> GetFolderTreeAsync(
        int rootEntryId,
        int depth,
        CancellationToken cancellationToken = default);
}
