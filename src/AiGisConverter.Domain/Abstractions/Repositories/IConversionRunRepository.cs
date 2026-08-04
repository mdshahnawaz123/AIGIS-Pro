using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;

namespace AiGisConverter.Domain.Abstractions.Repositories;

/// <summary>Persistence for <see cref="ConversionRun"/> aggregates.</summary>
public interface IConversionRunRepository : IRepository<ConversionRun, ConversionRunId>
{
    /// <summary>Loads the most recent run for a job.</summary>
    /// <param name="jobId">The job.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The latest run, or <see langword="null"/> when the job has never run.</returns>
    Task<ConversionRun?> GetLatestForJobAsync(
        ConversionJobId jobId,
        CancellationToken cancellationToken = default);

    /// <summary>Lists run history for a project, newest first.</summary>
    /// <param name="projectId">The project.</param>
    /// <param name="limit">Maximum number to return.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The run history.</returns>
    Task<IReadOnlyList<ConversionRun>> ListForProjectAsync(
        ProjectId projectId,
        int limit,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes runs finished before a cut-off.
    /// </summary>
    /// <remarks>
    /// Run history grows without bound on a machine doing nightly batches. Pruning is a repository
    /// operation rather than a loop over loaded aggregates because there may be hundreds of
    /// thousands of them.
    /// </remarks>
    /// <param name="finishedBeforeUtc">The cut-off.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of runs deleted.</returns>
    Task<int> PruneAsync(DateTimeOffset finishedBeforeUtc, CancellationToken cancellationToken = default);
}
