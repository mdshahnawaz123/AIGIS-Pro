using AiGisConverter.Data.Context;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Specifications;
using Microsoft.EntityFrameworkCore;

namespace AiGisConverter.Data.Repositories;

/// <summary>
/// Entity Framework implementation of the repository contract.
/// </summary>
/// <typeparam name="TEntity">The aggregate root type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <remarks>
/// Specifications reach the database as expression trees, so filtering happens in SQL. That was
/// the reason <c>ISpecification</c> returns an <c>Expression</c> rather than a delegate, and it is
/// why a run-history screen stays usable after a year of nightly batches.
/// </remarks>
public class Repository<TEntity, TId> : IRepository<TEntity, TId>
    where TEntity : class, IAggregateRoot
    where TId : notnull
{
    /// <summary>Initializes a new instance of the <see cref="Repository{TEntity, TId}"/> class.</summary>
    /// <param name="context">The database context.</param>
    public Repository(AiGisConverterDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
    }

    /// <summary>Gets the database context.</summary>
    protected AiGisConverterDbContext Context { get; }

    /// <summary>Gets the entity set.</summary>
    protected DbSet<TEntity> Set => Context.Set<TEntity>();

    /// <inheritdoc />
    public virtual async Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default) =>
        await Set.FindAsync([id], cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public virtual async Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return await Set.AsNoTracking()
            .Where(specification.ToExpression())
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual Task<int> CountAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return Set.AsNoTracking().CountAsync(specification.ToExpression(), cancellationToken);
    }

    /// <inheritdoc />
    public virtual Task<bool> AnyAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(specification);

        return Set.AsNoTracking().AnyAsync(specification.ToExpression(), cancellationToken);
    }

    /// <inheritdoc />
    public virtual async Task AddAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entity);

        await Set.AddAsync(entity, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public virtual void Update(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        Set.Update(entity);
    }

    /// <inheritdoc />
    public virtual void Remove(TEntity entity)
    {
        ArgumentNullException.ThrowIfNull(entity);

        Set.Remove(entity);
    }
}
