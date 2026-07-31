using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Adapters;

/// <summary>
/// Builds Laserfiche Repository API v1 endpoint URLs from the live
/// <see cref="LaserficheOptions"/>. Registered as a singleton; uses
/// <see cref="IOptionsMonitor{T}"/> so URL changes made via the Settings page
/// take effect immediately without an application restart.
/// </summary>
internal sealed class LaserficheApiAdapter : ILaserficheApiAdapter
{
    private readonly IOptionsMonitor<LaserficheOptions> _optionsMonitor;

    /// <summary>Initialises the adapter with a live options monitor.</summary>
    public LaserficheApiAdapter(IOptionsMonitor<LaserficheOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    /// <inheritdoc />
    public string ApiVersion => _optionsMonitor.CurrentValue.ApiVersion;

    /// <summary>
    /// Returns the base URL for all repository-scoped endpoints:
    /// <c>{ServerUrl}{ApiBasePath}/{ApiVersion}/Repositories/{repoId}</c>.
    /// The configured base path is included exactly once, even when a caller
    /// supplies a server URL that already ends with that path.
    /// </summary>
    private string RepoBase(string repositoryId)
    {
        return $"{BuildApiBase(_optionsMonitor.CurrentValue.ServerUrl)}/Repositories/{repositoryId}";
    }

    /// <summary>
    /// Returns the root API URL without a repository scope:
    /// <c>{ServerUrl}{ApiBasePath}/{ApiVersion}</c>.
    /// </summary>
    private string ApiBase()
    {
        return BuildApiBase(_optionsMonitor.CurrentValue.ServerUrl);
    }

    /// <inheritdoc />
    public string BuildRepositoriesUrl() =>
        $"{ApiBase()}/Repositories";

    /// <inheritdoc />
    public string BuildRepositoriesUrlFor(string serverUrl) =>
        $"{BuildApiBase(serverUrl)}/Repositories";

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
    public string BuildTokenUrlFor(string serverUrl, string repositoryId)
    {
        return $"{BuildApiBase(serverUrl)}/Repositories/{repositoryId}/Token";
    }

    /// <summary>
    /// Combines a server URL with the configured API base path and version.
    /// ServerUrl is normally scheme plus host, but older saved settings and
    /// connection-test form values may already contain ApiBasePath. Remove that
    /// exact trailing path before adding it so the final URL contains one copy.
    /// </summary>
    private string BuildApiBase(string serverUrl)
    {
        var options = _optionsMonitor.CurrentValue;
        var root = serverUrl.TrimEnd('/');
        var basePath = "/" + options.ApiBasePath.Trim('/');

        if (root.EndsWith(basePath, StringComparison.OrdinalIgnoreCase))
        {
            root = root[..^basePath.Length].TrimEnd('/');
        }

        return $"{root}{basePath}/{options.ApiVersion.Trim('/')}";
    }
}
