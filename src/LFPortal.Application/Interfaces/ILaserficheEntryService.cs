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
    /// Discovers the repository root entry ID dynamically.
    /// The root is NOT always entry 1 — it varies per server installation.
    /// Discovery order:
    ///   1. Path-based lookup — <c>GET /Entries?entryPath=%5C</c> (backslash = root in LF).
    ///   2. Entry 1 parentId check — if <c>parentId == 0</c> then 1 is the root.
    ///   3. Fallback to 1 with a warning.
    /// The result is cached in-process after the first successful discovery.
    /// </summary>
    Task<int> GetRootEntryIdAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves ALL direct children of the specified folder in a single API call
    /// (up to 1 000 entries). Unlike <see cref="GetEntryChildrenAsync"/>, this method
    /// does not paginate — it is optimised for the recursive folder scan used by the
    /// dashboard service. Uses the v1 OData-typed path confirmed in Swagger:
    /// <c>/Entries/{id}/Laserfiche.Repository.Folder/children</c>.
    /// </summary>
    /// <param name="entryId">Entry ID of the parent folder to enumerate.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task<IReadOnlyList<LFEntry>> GetAllFolderChildrenAsync(
        int entryId,
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
