using AiGisConverter.Data.Context;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using Microsoft.EntityFrameworkCore;

namespace AiGisConverter.Data.Repositories;

/// <summary>Persistence for <see cref="ConversionProject"/>.</summary>
public sealed class ConversionProjectRepository
    : Repository<ConversionProject, ProjectId>, IConversionProjectRepository
{
    /// <summary>Initializes a new instance of the <see cref="ConversionProjectRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public ConversionProjectRepository(AiGisConverterDbContext context)
        : base(context)
    {
    }

    /// <summary>
    /// Loads a project by identifier, with its jobs.
    /// </summary>
    /// <remarks>
    /// The aggregate is always loaded whole. A project without its jobs cannot enforce the rules
    /// that span them &#8212; refusing to queue when empty, rejecting a duplicate source &#8212;
    /// so handing one back would be handing back something that only looks like the aggregate.
    /// </remarks>
    /// <param name="id">The project identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The project, or <see langword="null"/> when it does not exist.</returns>
    public override Task<ConversionProject?> GetByIdAsync(
        ProjectId id,
        CancellationToken cancellationToken = default) =>
        GetWithJobsAsync(id, cancellationToken);

    /// <inheritdoc />
    public Task<ConversionProject?> GetWithJobsAsync(
        ProjectId id,
        CancellationToken cancellationToken = default) =>
        Set.Include(project => project.Jobs)
            .FirstOrDefaultAsync(project => project.Id == id, cancellationToken);

    /// <inheritdoc />
    public Task<ConversionProject?> FindByNameAsync(
        string name,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        // SQLite's default collation is case-sensitive; EF.Functions.Like is not, which matches
        // what a user means when they type a project name.
        return Set.Include(project => project.Jobs)
            .FirstOrDefaultAsync(project => EF.Functions.Like(project.Name, name), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ConversionProject>> ListRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return await Set.AsNoTracking()
            .OrderByDescending(project => project.ModifiedAtUtc ?? project.CreatedAtUtc)
            .Take(limit)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
