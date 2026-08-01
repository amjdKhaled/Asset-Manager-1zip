using LFPortal.Application.DTOs;

namespace LFPortal.Application.Interfaces;

/// <summary>
/// Manages Laserfiche Bearer token acquisition, caching, and invalidation.
/// Tokens are cached per repository and proactively refreshed before expiry.
/// </summary>
/// <remarks>
/// <para>
/// Callers never see credentials — they receive only an opaque Bearer token string.
/// Token acquisition calls <see cref="ICredentialProvider"/> internally; credentials
/// are not held in memory beyond the HTTP request that uses them.
/// </para>
/// <para>
/// The token cache is keyed by <see cref="RepositoryDescriptor.Key"/> so each
/// repository maintains a completely independent token lifecycle.
/// </para>
/// </remarks>
public interface ILaserficheAuthService
{
    /// <summary>
    /// Returns a valid Bearer token for the specified repository, acquiring a new one
    /// if the cache is empty or the cached token is within 60 seconds of expiry.
    /// </summary>
    /// <param name="repository">The repository to authenticate against.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>A raw Bearer token string, without the <c>Bearer </c> prefix.</returns>
    Task<string> GetTokenAsync(
        RepositoryDescriptor repository,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Removes the cached token for the specified repository, forcing re-authentication
    /// on the next call to <see cref="GetTokenAsync"/>.
    /// Call this after receiving a 401 response from the API to recover from token expiry.
    /// </summary>
    /// <param name="repository">The repository whose cached token should be discarded.</param>
    Task InvalidateTokenAsync(RepositoryDescriptor repository);

    /// <summary>
    /// Attempts to authenticate against the specified repository using the supplied
    /// credentials without consulting <see cref="ICredentialProvider"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// On success the acquired Bearer token is written to the memory cache under the same
    /// key used by <see cref="GetTokenAsync"/>, so subsequent domain-service calls will
    /// find a warm cache without prompting for credentials again.
    /// </para>
    /// <para>
    /// Returns <c>false</c> for credential failures (HTTP 4xx).
    /// Infrastructure errors (network failures, HTTP 5xx) are propagated as exceptions.
    /// The password is never logged.
    /// </para>
    /// </remarks>
    /// <param name="repository">The repository to authenticate against.</param>
    /// <param name="username">Laserfiche username.</param>
    /// <param name="password">Plain-text password (may be an empty string).</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// <c>true</c> when the Laserfiche API accepted the credentials;
    /// <c>false</c> when it rejected them.
    /// </returns>
    Task<bool> TryAuthenticateAsync(
        RepositoryDescriptor repository,
        string username,
        string password,
        CancellationToken cancellationToken = default);
}
