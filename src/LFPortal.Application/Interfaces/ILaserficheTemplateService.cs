using LFPortal.Domain.Entities;

namespace LFPortal.Application.Interfaces;

/// <summary>
/// Retrieves template definitions from the Laserfiche Repository API.
/// Template definitions describe the metadata field schemas available
/// in the connected repository.
/// </summary>
public interface ILaserficheTemplateService
{
    /// <summary>
    /// Returns all template definitions from the active repository.
    /// Calls <c>GET /TemplateDefinitions</c> on the Laserfiche API.
    /// Returns an empty list when no templates are configured or when
    /// the endpoint is unavailable.
    /// </summary>
    Task<IReadOnlyList<LFTemplateDefinition>> GetTemplateDefinitionsAsync(
        CancellationToken cancellationToken = default);
}
