using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Adapters;

/// <summary>
/// Builds Laserfiche Repository API v2 endpoint URLs from the configured
/// <see cref="LaserficheOptions"/>. Registered as a singleton — all state is
/// derived from immutable options at startup.
/// </summary>
/// <remarks>
/// To add support for a future API version: implement <see cref="ILaserficheApiAdapter"/>
/// in a new class (e.g. <c>LaserficheV3ApiAdapter</c>) and change the DI registration
/// in <c>ServiceCollectionExtensions</c>. No service code changes required.
/// </remarks>
internal sealed class LaserficheV2ApiAdapter : ILaserficheApiAdapter
{
    private readonly LaserficheOptions _options;

    /// <summary>
    /// Initialises the adapter with bound options.
    /// </summary>
    public LaserficheV2ApiAdapter(IOptions<LaserficheOptions> options)
    {
        _options = options.Value;
    }

    /// <inheritdoc />
    public string ApiVersion => _options.ApiVersion;

    /// <summary>
    /// Returns the base URL for all repository-scoped endpoints:
    /// <c>{ServerUrl}{ApiBasePath}/{ApiVersion}/Repositories/{repoId}</c>.
    /// </summary>
    private string RepoBase(string repositoryId) =>
        $"{_options.ServerUrl.TrimEnd('/')}{_options.ApiBasePath}/{_options.ApiVersion}/Repositories/{repositoryId}";

    /// <summary>
    /// Returns the root API URL without a repository scope:
    /// <c>{ServerUrl}{ApiBasePath}/{ApiVersion}</c>.
    /// </summary>
    private string ApiBase() =>
        $"{_options.ServerUrl.TrimEnd('/')}{_options.ApiBasePath}/{_options.ApiVersion}";

    /// <inheritdoc />
    public string BuildRepositoriesUrl() =>
        $"{ApiBase()}/Repositories";

    /// <inheritdoc />
    public string BuildTokenUrl(string repositoryId) =>
        $"{RepoBase(repositoryId)}/Token";

    /// <inheritdoc />
    public string BuildEntryUrl(string repositoryId, int entryId, EntryResource resource) =>
        resource switch
        {
            EntryResource.Details  => $"{RepoBase(repositoryId)}/Entries/{entryId}",
            EntryResource.Fields   => $"{RepoBase(repositoryId)}/Entries/{entryId}/fields",
            EntryResource.Tags     => $"{RepoBase(repositoryId)}/Entries/{entryId}/tags",
            EntryResource.Children => $"{RepoBase(repositoryId)}/Entries/{entryId}/children",
            EntryResource.Edoc     => $"{RepoBase(repositoryId)}/Entries/{entryId}/edoc",
            EntryResource.Pages    => $"{RepoBase(repositoryId)}/Entries/{entryId}/pages",
            _ => throw new ArgumentOutOfRangeException(nameof(resource), resource, "Unknown entry resource.")
        };

    /// <inheritdoc />
    public string BuildPageImageUrl(string repositoryId, int entryId, int pageNumber) =>
        $"{RepoBase(repositoryId)}/Entries/{entryId}/pages/{pageNumber}/image";

    /// <inheritdoc />
    public string BuildSearchUrl(string repositoryId, SearchType searchType) =>
        searchType switch
        {
            SearchType.Simple   => $"{RepoBase(repositoryId)}/SimpleSearches",
            SearchType.Advanced => $"{RepoBase(repositoryId)}/Entries/Search",
            _ => throw new ArgumentOutOfRangeException(nameof(searchType), searchType, "Unknown search type.")
        };

    /// <inheritdoc />
    public string BuildTaskStatusUrl(string repositoryId, string operationToken) =>
        $"{RepoBase(repositoryId)}/Tasks/{operationToken}";

    /// <inheritdoc />
    public string BuildSearchResultsUrl(string repositoryId, string operationToken) =>
        $"{RepoBase(repositoryId)}/SearchResults/{operationToken}";

    /// <inheritdoc />
    public string BuildRepositoryInfoUrl(string repositoryId) =>
        $"{ApiBase()}/Repositories/{repositoryId}";
}
