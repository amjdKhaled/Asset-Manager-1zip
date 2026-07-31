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
internal interface ILaserficheApiAdapter
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
    /// Builds the URL for a search endpoint, e.g.
    /// <c>POST /Repositories/{repoId}/SimpleSearches</c> or
    /// <c>POST /Repositories/{repoId}/Entries/Search</c>.
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
    /// Builds the URL for repository information:
    /// <c>GET /Repositories/{repoId}</c>.
    /// </summary>
    string BuildRepositoryInfoUrl(string repositoryId);
}
