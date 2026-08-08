using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Adapters;

/// <summary>
/// Builds Laserfiche Repository API v1 endpoint URLs from the live
/// <see cref="LaserficheOptions"/>. Registered as a singleton; uses
/// <see cref="IOptionsMonitor{T}"/> so URL changes made via the Settings page
/// take effect immediately without an application restart.
/// </summary>
public sealed class LaserficheApiAdapter : ILaserficheApiAdapter
{
    private readonly IOptionsMonitor<LaserficheOptions> _optionsMonitor;

    /// <summary>Initialises the adapter with a live options monitor.</summary>
    public LaserficheApiAdapter(IOptionsMonitor<LaserficheOptions> optionsMonitor)
    {
        _optionsMonitor = optionsMonitor;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always the EFFECTIVE version (explicit pin, or the auto-detected value) —
    /// never the raw <c>Auto</c> sentinel, so every URL this adapter builds is a
    /// concrete <c>v1</c>/<c>v2</c> path.
    /// </remarks>
    public string ApiVersion => _optionsMonitor.CurrentValue.EffectiveApiVersion;

    /// <summary>
    /// Returns the base URL for all repository-scoped endpoints:
    /// <c>{ServerUrl}{ApiBasePath}/{ApiVersion}/Repositories/{repoId}</c>.
    /// The configured base path is included exactly once, even when a caller
    /// supplies a server URL that already ends with that path.
    /// </summary>
    private string RepoBase(string repositoryId)
    {
        // Repository IDs are path segments and must be percent-encoded so that
        // names containing spaces, ampersands, or other reserved characters
        // produce a valid URL rather than a malformed one the server may reject.
        return $"{BuildApiBase(_optionsMonitor.CurrentValue.ServerUrl)}/Repositories/{Uri.EscapeDataString(repositoryId)}";
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
            // formatValue=false is required for the document field-values response.
            // It returns the actual assigned field values without server-side
            // display formatting, so the values can be joined to FieldDefinitions.
            EntryResource.Fields   => $"{RepoBase(repositoryId)}/Entries/{entryId}/fields?formatValue=false",
            EntryResource.Tags     => $"{RepoBase(repositoryId)}/Entries/{entryId}/tags",
            EntryResource.Children       => $"{RepoBase(repositoryId)}/Entries/{entryId}/children",
            // FolderChildren is version-specific — delegate to the dedicated builder
            // so V1 and V2 always get the correct path.
            EntryResource.FolderChildren => BuildFolderChildrenUrl(repositoryId, entryId),
            EntryResource.Edoc     => $"{RepoBase(repositoryId)}/Entries/{entryId}/Laserfiche.Repository.Document/edoc",
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
            // v1 synchronous search — results returned inline, no polling required.
            SearchType.Simple   => $"{RepoBase(repositoryId)}/SimpleSearches",
            // v1 async search — submit, poll Tasks/{token}, fetch SearchResults/{token}.
            // NOTE: Entries/Search is a v2-only path; v1 uses /Searches.
            SearchType.Advanced => $"{RepoBase(repositoryId)}/Searches",
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

    /// <inheritdoc />
    /// <remarks>
    /// <b>Version-aware.</b>  The Laserfiche Repository API changed the folder-children
    /// path between V1 and V2:
    /// <list type="bullet">
    ///   <item><b>V1:</b> <c>/Entries/{id}/Laserfiche.Repository.Folder/children</c> — OData-typed cast path.</item>
    ///   <item><b>V2:</b> <c>/Entries/{id}/Folder/Children</c> — simplified path; V1's OData-typed
    ///         path returns HTTP 404 on V2 servers.</item>
    /// </list>
    /// The V2 path includes <c>groupByEntryType=false&amp;formatFieldValues=false</c> exactly as
    /// shown in the server Swagger documentation.
    /// </remarks>
    public string BuildFolderChildrenUrl(string repositoryId, int entryId)
    {
        var repoBase = RepoBase(repositoryId);

        // V2 uses a completely different path from V1.
        // Confirmed from the server Swagger: GET /v2/.../Entries/{id}/Folder/Children
        if (ApiVersion.Equals("v2", StringComparison.OrdinalIgnoreCase))
            return $"{repoBase}/Entries/{entryId}/Folder/Children?groupByEntryType=false&formatFieldValues=false";

        // V1: OData-typed cast path.
        return $"{repoBase}/Entries/{entryId}/Laserfiche.Repository.Folder/children";
    }

    /// <inheritdoc />
    public string BuildTemplateDefinitionsUrl(string repositoryId) =>
        $"{RepoBase(repositoryId)}/TemplateDefinitions";

    /// <inheritdoc />
    public string BuildFieldDefinitionsUrl(string repositoryId) =>
        $"{RepoBase(repositoryId)}/FieldDefinitions";

    /// <inheritdoc />
    public string BuildEntryByPathUrl(string repositoryId, string fullPath) =>
        $"{RepoBase(repositoryId)}/Entries/ByPath?fullPath={Uri.EscapeDataString(fullPath)}";

    /// <inheritdoc />
    public int GetConfiguredRootEntryId() =>
        _optionsMonitor.CurrentValue.RootEntryId;

    /// <inheritdoc />
    /// <remarks>
    /// Always targets the <c>v2</c> path regardless of <see cref="LaserficheOptions.EffectiveApiVersion"/>.
    /// LFDS authorization codes must be exchanged at the V2 token endpoint; the resulting
    /// Bearer token is accepted by V1 resource endpoints on the same API Server.
    /// </remarks>
    public string BuildTokenUrlV2(string repositoryId)
    {
        var options  = _optionsMonitor.CurrentValue;
        var root     = options.ServerUrl.TrimEnd('/');
        var basePath = "/" + options.ApiBasePath.Trim('/');

        if (root.EndsWith(basePath, StringComparison.OrdinalIgnoreCase))
            root = root[..^basePath.Length].TrimEnd('/');

        return $"{root}{basePath}/v2/Repositories/{Uri.EscapeDataString(repositoryId)}/Token";
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

        return $"{root}{basePath}/{options.EffectiveApiVersion.Trim('/')}";
    }
}
