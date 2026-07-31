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
}
