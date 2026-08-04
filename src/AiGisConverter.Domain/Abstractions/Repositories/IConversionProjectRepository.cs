using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;

namespace AiGisConverter.Domain.Abstractions.Repositories;

/// <summary>Persistence for <see cref="ConversionProject"/> aggregates.</summary>
public interface IConversionProjectRepository : IRepository<ConversionProject, ProjectId>
{
    /// <summary>Loads a project together with its jobs.</summary>
    /// <param name="id">The project identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The project, or <see langword="null"/> when it does not exist.</returns>
    Task<ConversionProject?> GetWithJobsAsync(ProjectId id, CancellationToken cancellationToken = default);

    /// <summary>Finds a project by name, matched case-insensitively.</summary>
    /// <param name="name">The project name.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The project, or <see langword="null"/> when no project has that name.</returns>
    Task<ConversionProject?> FindByNameAsync(string name, CancellationToken cancellationToken = default);

    /// <summary>Lists projects most recently modified first, for the start screen.</summary>
    /// <param name="limit">Maximum number to return.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The most recently touched projects.</returns>
    Task<IReadOnlyList<ConversionProject>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
