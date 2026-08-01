using LFPortal.Domain.Entities;

namespace LFPortal.Application.Interfaces;

/// <summary>
/// Retrieves repository-wide field definitions from the Laserfiche Repository API.
/// Field definitions describe the schema of each metadata field (name, type, required,
/// multi-value) but do NOT contain per-document values — use
/// <see cref="ILaserficheEntryService.GetEntryFieldsAsync"/> for actual values.
/// </summary>
public interface ILaserficheFieldDefinitionService
{
    /// <summary>
    /// Returns all field definitions available in the currently configured repository.
    /// Results are keyed by <see cref="LFFieldDefinition.Id"/> so callers can look up
    /// names for a given <c>fieldDefinitionId</c> from an entry fields response.
    /// </summary>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// A read-only dictionary mapping numeric field-definition ID → definition record.
    /// Empty if the repository has no custom fields or if the endpoint is unavailable.
    /// </returns>
    Task<IReadOnlyDictionary<int, LFFieldDefinition>> GetFieldDefinitionsAsync(
        CancellationToken cancellationToken = default);
}
