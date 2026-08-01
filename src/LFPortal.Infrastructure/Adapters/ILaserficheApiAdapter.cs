namespace LFPortal.Infrastructure.Adapters;

/// <summary>
/// Builds absolute URLs for every Laserfiche Repository API endpoint used by the portal.
/// All URL construction is centralised here so that changing the API server address,
/// virtual directory path, or API version version touches only this interface and its
/// single implementation — no service code changes are required.
/// </summary>
/// <remarks>
/// ADR-004 documents why this abstraction exists and how to add support for a future
/// Laserfiche API version without disrupting existing service code.
/// </remarks>
public interface ILaserficheApiAdapter
{
    /// <summary>The API version this adapter targets, e.g. <c>v2</c>.</summary>
    string ApiVersion { get; }

    /// <summary>
    /// Builds the URL for <c>GET /Repositories</c> — the repository discovery endpoint.
    /// Note: this endpoint must be explicitly enabled in the API Server's
    /// <c>appsettings.json</c> (<c>EnableGetRepositoryListApi: true</c>).
    /// </summary>
    string BuildRepositoriesUrl();

    /// <summary>
    /// Builds the URL for <c>GET /Repositories</c> using an explicitly supplied
    /// server URL. Used by the Settings connection test before configuration is saved.
    /// </summary>
    string BuildRepositoriesUrlFor(string serverUrl);

    /// <summary>
    /// Builds the URL for <c>POST /Repositories/{repoId}/Token</c> — the password-grant
    /// token issuance endpoint.
    /// </summary>
    string BuildTokenUrl(string repositoryId);

    /// <summary>
    /// Builds the URL for an entry resource endpoint, e.g.
    /// <c>GET /Repositories/{repoId}/Entries/{entryId}</c> or
    /// <c>GET /Repositories/{repoId}/Entries/{entryId}/fields</c>.
    /// </summary>
    string BuildEntryUrl(string repositoryId, int entryId, EntryResource resource);

    /// <summary>
    /// Builds the URL for a single page image:
    /// <c>GET /Repositories/{repoId}/Entries/{entryId}/pages/{pageNumber}/image</c>.
    /// </summary>
    string BuildPageImageUrl(string repositoryId, int entryId, int pageNumber);

    /// <summary>
    /// Builds the URL for a search endpoint:
    /// <c>POST /Repositories/{repoId}/SimpleSearches</c> (simple, synchronous) or
    /// <c>POST /Repositories/{repoId}/Searches</c> (advanced, async long-operation).
    /// Note: <c>Entries/Search</c> is a v2-only path and must not be used here.
    /// </summary>
    string BuildSearchUrl(string repositoryId, SearchType searchType);

    /// <summary>
    /// Builds the URL for a long-operation task status check:
    /// <c>GET /Repositories/{repoId}/Tasks/{operationToken}</c>.
    /// </summary>
    string BuildTaskStatusUrl(string repositoryId, string operationToken);

    /// <summary>
    /// Builds the URL for retrieving search results after a long operation completes:
    /// <c>GET /Repositories/{repoId}/SearchResults/{operationToken}</c>.
    /// </summary>
    string BuildSearchResultsUrl(string repositoryId, string operationToken);

    /// <summary>
    /// Builds a token URL using an explicitly-supplied server URL instead of the stored
    /// configuration. Used when testing a connection with credentials that have not yet been
    /// saved, so the test hits exactly what was typed into the form.
    /// </summary>
    string BuildTokenUrlFor(string serverUrl, string repositoryId);

    /// <summary>
    /// Builds the URL for listing direct children of a folder entry using the
    /// Laserfiche v1 OData-typed path:
    /// <c>GET /Repositories/{repoId}/Entries/{entryId}/Laserfiche.Repository.Folder/children</c>.
    /// No query parameters — this installation rejects $top, $skip, $count, and $select with HTTP 400.
    /// </summary>
    string BuildFolderChildrenUrl(string repositoryId, int entryId);

    /// <summary>
    /// Builds the URL for the template definitions endpoint:
    /// <c>GET /Repositories/{repoId}/TemplateDefinitions</c>.
    /// </summary>
    string BuildTemplateDefinitionsUrl(string repositoryId);

    /// <summary>
    /// Builds the URL for the ByPath entry lookup endpoint:
    /// <c>GET /Repositories/{repoId}/Entries/ByPath?fullPath={encodedPath}</c>.
    /// Pass <c>%5C</c> (backslash) as the path to resolve the repository root.
    /// </summary>
    string BuildEntryByPathUrl(string repositoryId, string fullPath);

    /// <summary>
    /// Returns the administrator-configured root entry ID from <c>appsettings.json</c>
    /// (defaults to <c>1</c>). When greater than zero, this value is used directly
    /// and ByPath auto-discovery is skipped.
    /// </summary>
    int GetConfiguredRootEntryId();
}
