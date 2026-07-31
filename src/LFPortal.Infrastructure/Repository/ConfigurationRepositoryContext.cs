using LFPortal.Application.DTOs;
using LFPortal.Application.Interfaces;
using LFPortal.Infrastructure.Options;
using Microsoft.Extensions.Options;

namespace LFPortal.Infrastructure.Repository;

/// <summary>
/// Resolves the active repository from <see cref="LaserficheOptions"/> bound at startup.
/// Supports single-repository deployments only. To support multiple repositories,
/// implement <see cref="IRepositoryContext"/> with a session-backed store and register it
/// in place of this class — no service or controller code changes required.
/// </summary>
internal sealed class ConfigurationRepositoryContext : IRepositoryContext
{
    private readonly RepositoryDescriptor _descriptor;

    /// <summary>
    /// Initialises the context by building a <see cref="RepositoryDescriptor"/> from
    /// the bound <see cref="LaserficheOptions"/>.
    /// </summary>
    public ConfigurationRepositoryContext(IOptions<LaserficheOptions> options)
    {
        var opt = options.Value;
        _descriptor = new RepositoryDescriptor(
            Key: "default",
            ServerUrl: $"{opt.ServerUrl.TrimEnd('/')}{opt.ApiBasePath}",
            RepositoryId: opt.RepositoryId,
            DisplayName: opt.EffectiveDisplayName);
    }

    /// <inheritdoc />
    public Task<RepositoryDescriptor> GetActiveRepositoryAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_descriptor);

    /// <inheritdoc />
    public Task<IReadOnlyList<RepositoryDescriptor>> GetAllRepositoriesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<RepositoryDescriptor>>([_descriptor]);
}
