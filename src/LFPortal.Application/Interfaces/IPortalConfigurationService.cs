namespace LFPortal.Application.Interfaces;

/// <summary>
/// Persists portal-level connection configuration to a writable local file and
/// reports the current credential storage status. Implemented in the Infrastructure
/// layer; registered as a singleton.
/// </summary>
/// <remarks>
/// Only the non-sensitive connection settings (ServerUrl, RepositoryId, DisplayName)
/// are written by this service. Credentials are always stored via
/// <see cref="ICredentialProvider.StoreCredentialsAsync"/>; they never touch this service.
/// </remarks>
public interface IPortalConfigurationService
{
    /// <summary>
    /// Writes ServerUrl, RepositoryId, and DisplayName to the writable
    /// <c>config/laserfiche.json</c> file. The <c>IOptionsMonitor</c> pipeline
    /// detects the file change and reloads options automatically — no restart needed.
    /// </summary>
    Task SaveConnectionSettingsAsync(
        string serverUrl,
        string repositoryId,
        string displayName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns <c>true</c> when a secure credential file exists for the default
    /// repository key (DPAPI on Windows, Data Protection on non-Windows).
    /// </summary>
    bool HasSavedCredentials();

    /// <summary>
    /// Returns <c>true</c> when both <c>LF_USERNAME</c> and <c>LF_PASSWORD</c>
    /// environment variables are set. Indicates the active fallback path.
    /// </summary>
    bool HasEnvironmentVariableCredentials();
}
