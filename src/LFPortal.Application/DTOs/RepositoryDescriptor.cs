namespace LFPortal.Application.DTOs;

/// <summary>
/// Identifies a Laserfiche repository that the portal is configured to connect to.
/// Passed between the Application and Infrastructure layers to route API calls and
/// scope token-cache entries to the correct repository.
/// </summary>
/// <param name="Key">Unique configuration key used as the token-cache key. Never displayed to users.</param>
/// <param name="ServerUrl">Base URL of the Laserfiche API Server, e.g. <c>https://lf-server/LFRepositoryAPI</c>.</param>
/// <param name="RepositoryId">Repository identifier used in API request paths, e.g. <c>Documents</c>.</param>
/// <param name="DisplayName">Human-readable name shown in the portal UI.</param>
public sealed record RepositoryDescriptor(
    string Key,
    string ServerUrl,
    string RepositoryId,
    string DisplayName);
