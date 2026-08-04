using AiGisConverter.Data.Context;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Data.Repositories;

/// <summary>Persistence for <see cref="ConversionRun"/>.</summary>
public sealed class ConversionRunRepository
    : Repository<ConversionRun, ConversionRunId>, IConversionRunRepository
{
    private readonly ILogger<ConversionRunRepository> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConversionRunRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="logger">Logger for pruning diagnostics.</param>
    public ConversionRunRepository(AiGisConverterDbContext context, ILogger<ConversionRunRepository> logger)
        : base(context)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<ConversionRun?> GetLatestForJobAsync(
        ConversionJobId jobId,
        CancellationToken cancellationToken = default) =>
        Set.AsNoTracking()
            .Where(run => run.JobId == jobId)
            .OrderByDescending(run => run.StartedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversionRun>> ListForProjectAsync(
        ProjectId projectId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await Set.AsNoTracking()
            .Where(run => run.ProjectId == projectId)
            .OrderByDescending(run => run.StartedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Deletes runs finished before a cut-off, and the findings that belong to them.
    /// </summary>
    /// <remarks>
    /// Executed as a set-based delete rather than by loading aggregates. Nightly batches accumulate
    /// hundreds of thousands of runs, and materialising them to call <c>Remove</c> on each would
    /// turn routine maintenance into an out-of-memory failure.
    /// </remarks>
    /// <param name="finishedBeforeUtc">The cut-off.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of runs deleted.</returns>
    public async Task<int> PruneAsync(
        DateTimeOffset finishedBeforeUtc,
        CancellationToken cancellationToken = default)
    {
        // SQLite's EF Core provider cannot translate nullable DateTimeOffset comparisons in LINQ.
        // We use raw SQL instead; SQLite stores DateTimeOffset as ISO 8601 text, which sorts correctly.
        string cutOffText = finishedBeforeUtc.ToString("o");

        // Findings first: they reference the run, and SQLite will not enforce the order for us.
        await Context.Database
            .ExecuteSqlRawAsync(
                "DELETE FROM ValidationIssues WHERE RunId IN " +
                "(SELECT Id FROM Runs WHERE FinishedAtUtc IS NOT NULL AND FinishedAtUtc < {0})",
                new object[] { cutOffText },
                cancellationToken)
            .ConfigureAwait(false);

        int deleted = await Context.Database
            .ExecuteSqlRawAsync(
                "DELETE FROM Runs WHERE FinishedAtUtc IS NOT NULL AND FinishedAtUtc < {0}",
                new object[] { cutOffText },
                cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Pruned {RunCount} runs finished before {CutOff:u}.",
            deleted,
            finishedBeforeUtc);

        return deleted;
    }
}
