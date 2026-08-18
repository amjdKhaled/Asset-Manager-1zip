using System.ComponentModel.DataAnnotations;
using LFPortal.Infrastructure.OAuth;

namespace LFPortal.Infrastructure.Options;

/// <summary>
/// Selects which credential provider implementation is used at runtime.
/// Configured via <c>Laserfiche:CredentialProvider</c> in <c>appsettings.json</c>.
/// </summary>
public enum CredentialProviderType
{
    /// <summary>
    /// Windows Data Protection API (DPAPI). Default for production on Windows Server.
    /// Credentials are encrypted with the machine key; usable only on the same machine.
    /// </summary>
    DPAPI,

    /// <summary>
    /// Environment variables <c>LF_USERNAME</c> and <c>LF_PASSWORD</c>.
    /// Supported on all platforms. Recommended for development and non-Windows environments.
    /// </summary>
    Environment
}

public enum LaserficheAuthenticationMode
{
    RepositoryPassword,
    LfdsSso,
}

/// <summary>
/// Configuration options for the Laserfiche Repository API connection.
/// Bound from the <c>Laserfiche</c> section in <c>appsettings.json</c> at startup.
/// </summary>
/// <remarks>
/// Credentials are intentionally absent from this class. They are sourced at runtime
/// by the registered <see cref="ICredentialProvider"/> implementation, never stored
/// in configuration files as plain text.
/// </remarks>
public sealed class LaserficheOptions
{
    /// <summary>Configuration section name used during DI binding.</summary>
    public const string SectionName = "Laserfiche";

    /// <summary>
    /// Base URL of the Laserfiche API Server, e.g. <c>https://your-lf-server.example.com</c>.
    /// Do not include the <c>/LFRepositoryAPI</c> path here.
    /// Intentionally NOT marked <c>[Required]</c>: on a clean machine the app must be able
    /// to start unconfigured (health check reports the missing URL; the installer wizard or
    /// Settings page supplies it).  A <c>[Required]</c> attribute combined with
    /// <c>ValidateOnStart</c> would crash the site before an administrator could configure it.
    /// </summary>
    public string ServerUrl { get; set; } = string.Empty;

    /// <summary>Selects direct Repository API password login or LFDS PKCE SSO.</summary>
    public LaserficheAuthenticationMode AuthenticationMode { get; set; } =
        LaserficheAuthenticationMode.RepositoryPassword;

    /// <summary>
    /// Explicit opt-in for the legacy LFDS authorization-code endpoints. Dashboard
    /// launches never use them; the default keeps LFDS disabled even when stale SSO
    /// URL settings remain in an upgraded installation.
    /// </summary>
    public bool EnableLfdsSso { get; set; }

    /// <summary>Public browser origin of the Dashboard, used for every OAuth callback.</summary>
    public string DashboardPublicBaseUrl { get; set; } = string.Empty;

    /// <summary>
    /// Optional fallback repository identifier, e.g. <c>Documents</c>.
    /// The repository is normally supplied per session at runtime — by the
    /// Laserfiche Desktop/Web Client (<c>?repository=</c>) or by user selection
    /// on the login page.  When set here it only serves as the default for
    /// direct browser access; when empty, direct browser users choose a
    /// repository at login.  Case-sensitive.
    /// </summary>
    public string RepositoryId { get; set; } = string.Empty;

    /// <summary>
    /// Repository identifiers users may select on the Dashboard password gateway.
    /// Values are configuration-owned; arbitrary repository names submitted by a
    /// browser are rejected before credentials are sent to Repository API.
    /// </summary>
    public List<string> Repositories { get; set; } = [];

    /// <summary>Normalized configured repository choices, including the legacy default.</summary>
    public IReadOnlyList<string> EffectiveRepositories => Repositories
        .Append(RepositoryId)
        .Where(static value => !string.IsNullOrWhiteSpace(value))
        .Select(static value => value.Trim())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    /// <summary>
    /// Human-readable label shown in the portal UI to identify this repository.
    /// Defaults to the <see cref="RepositoryId"/> when not explicitly set.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// IIS virtual directory path where the Laserfiche API Server is installed.
    /// Defaults to <c>/LFRepositoryAPI</c>; change only if installed at a non-standard path.
    /// </summary>
    public string ApiBasePath { get; set; } = "/LFRepositoryAPI";

    /// <summary>Sentinel value meaning "probe the server and detect the API version".</summary>
    public const string ApiVersionAuto = "Auto";

    /// <summary>
    /// Configured API version: <c>Auto</c> (default — detect by probing the server),
    /// <c>v1</c>, or <c>v2</c>. Never used directly to build URLs — always go through
    /// <see cref="EffectiveApiVersion"/>, which resolves <c>Auto</c> to the detected
    /// version. Existing installations that persisted <c>v1</c> keep that explicit
    /// pin (backward compatible).
    /// </summary>
    public string ApiVersion { get; set; } = ApiVersionAuto;

    /// <summary>
    /// The API version detected by probing the server when <see cref="ApiVersion"/> is
    /// <c>Auto</c>. Persisted in the runtime settings file by the detection service so
    /// the result survives restarts and is visible on the Settings page. Empty until
    /// detection has run.
    /// </summary>
    public string DetectedApiVersion { get; set; } = string.Empty;

    /// <summary>
    /// The version actually used to build every API URL:
    /// an explicit <see cref="ApiVersion"/> (<c>v1</c>/<c>v2</c>) wins; in <c>Auto</c>
    /// mode the persisted <see cref="DetectedApiVersion"/> is used, falling back to
    /// <c>v1</c> (the broadly supported on-premises version) until detection completes.
    /// </summary>
    public string EffectiveApiVersion =>
        !IsAutoApiVersion ? ApiVersion.Trim()
        : !string.IsNullOrWhiteSpace(DetectedApiVersion) ? DetectedApiVersion.Trim()
        : "v1";

    /// <summary>True when the configured version is the Auto-Detect sentinel.</summary>
    public bool IsAutoApiVersion =>
        string.IsNullOrWhiteSpace(ApiVersion) ||
        string.Equals(ApiVersion.Trim(), ApiVersionAuto, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// HTTP request timeout in seconds for all Laserfiche API calls.
    /// Defaults to 30. Increase for slow networks or large document downloads.
    /// </summary>
    [Range(5, 300)]
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Credential storage back-end to use. Defaults to <see cref="CredentialProviderType.DPAPI"/>
    /// on Windows and <see cref="CredentialProviderType.Environment"/> on non-Windows platforms.
    /// Override explicitly in <c>appsettings.json</c> when needed.
    /// </summary>
    public CredentialProviderType CredentialProvider { get; set; } =
        OperatingSystem.IsWindows()
            ? CredentialProviderType.DPAPI
            : CredentialProviderType.Environment;

    /// <summary>
    /// The entry ID of the repository root folder. Defaults to <c>1</c>.
    /// Override in <c>appsettings.json</c> or via the Settings page if the root
    /// entry on your server is not ID 1 (e.g. set to 250 for some installations).
    /// When set, this value is used directly and automatic ByPath root discovery
    /// is skipped.
    /// </summary>
    public int RootEntryId { get; set; } = 1;

    /// <summary>
    /// Returns <see cref="DisplayName"/> when set; otherwise falls back to <see cref="RepositoryId"/>.
    /// </summary>
    public string EffectiveDisplayName =>
        string.IsNullOrWhiteSpace(DisplayName) ? RepositoryId : DisplayName;

    /// <summary>
    /// LFDS OAuth2 / Authorization Code SSO settings.
    /// Configure <see cref="LaserficheOAuthOptions.LfdsBaseUrl"/> to enable SSO.
    /// Nested under <c>Laserfiche:Sso</c> in <c>appsettings.json</c>.
    /// </summary>
    public LaserficheOAuthOptions Sso { get; set; } = new();

    /// <summary>
    /// Repository API endpoint that initiates the V2 authorization-code flow.
    /// The API Server delegates to LFDS/WebSTS; browser clients must not call
    /// the LFDS STS application directly.
    /// </summary>
    public string SsoAuthorizationEndpoint
    {
        get
        {
            if (string.IsNullOrWhiteSpace(ServerUrl))
                return string.Empty;

            var serverUrl = ServerUrl.TrimEnd('/');
            var apiBasePath = "/" + ApiBasePath.Trim('/');

            // ServerUrl is configured as an origin, while ApiBasePath owns the
            // Repository API virtual directory. Tolerate an older persisted
            // ServerUrl that already contains that directory without duplicating it.
            var apiRoot = serverUrl.EndsWith(apiBasePath, StringComparison.OrdinalIgnoreCase)
                ? serverUrl
                : serverUrl + apiBasePath;

            return $"{apiRoot}/v2/Authorize";
        }
    }

    /// <summary>Deterministic Dashboard callback URI used throughout an OAuth flow.</summary>
    public string SsoCallbackUrl => string.IsNullOrWhiteSpace(DashboardPublicBaseUrl)
        ? string.Empty
        : $"{DashboardPublicBaseUrl.TrimEnd('/')}/login/Callback";

    /// <summary>Repository-specific V2 authorization-code token endpoint.</summary>
    public string GetSsoTokenEndpoint(string repositoryId)
    {
        var authorize = SsoAuthorizationEndpoint;
        if (!authorize.EndsWith("Authorize", StringComparison.OrdinalIgnoreCase))
            return string.Empty;
        return authorize[..^"Authorize".Length] + "Repositories/" +
            Uri.EscapeDataString(repositoryId) + "/Token";
    }

    /// <summary>Detects pasted Markdown links, which are never valid URL settings.</summary>
    public IReadOnlyList<string> MarkdownConfigurationKeys()
    {
        static bool Invalid(string? value) => value?.IndexOfAny(['[', ']', '(', ')']) >= 0;
        var invalid = new List<string>();
        if (Invalid(ServerUrl)) invalid.Add("Laserfiche:ServerUrl");
        if (Invalid(ApiBasePath)) invalid.Add("Laserfiche:ApiBasePath");
        if (Invalid(DashboardPublicBaseUrl)) invalid.Add("Laserfiche:DashboardPublicBaseUrl");
        if (Invalid(Sso.LfdsBaseUrl)) invalid.Add("Laserfiche:Sso:LfdsBaseUrl");
        if (Invalid(Sso.RedirectUri)) invalid.Add("Laserfiche:Sso:RedirectUri");
        return invalid;
    }
}
