using System.ComponentModel.DataAnnotations;

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
    /// Human-readable label shown in the portal UI to identify this repository.
    /// Defaults to the <see cref="RepositoryId"/> when not explicitly set.
    /// </summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// IIS virtual directory path where the Laserfiche API Server is installed.
    /// Defaults to <c>/LFRepositoryAPI</c>; change only if installed at a non-standard path.
    /// </summary>
    public string ApiBasePath { get; set; } = "/LFRepositoryAPI";

    /// <summary>
    /// API version path segment. Defaults to <c>v1</c> — the version supported by
    /// Laserfiche API Server on-premises installations.
    /// </summary>
    public string ApiVersion { get; set; } = "v1";

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
}
