using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Specifications;

namespace AiGisConverter.Domain.Abstractions.Repositories;

/// <summary>
/// Read access to an aggregate root.
/// </summary>
/// <typeparam name="TEntity">The aggregate root type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <remarks>
/// Repositories exist for aggregate roots only. Entities inside an aggregate are reached through
/// their root, which is what keeps the root's invariants enforceable &#8212; a repository for
/// <c>ConversionJob</c> would allow a job to be saved in a state its project would have rejected.
/// </remarks>
public interface IReadOnlyRepository<TEntity, in TId>
    where TEntity : class, IAggregateRoot
    where TId : notnull
{
    /// <summary>Loads an aggregate by identifier.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The aggregate, or <see langword="null"/> when it does not exist.</returns>
    Task<TEntity?> GetByIdAsync(TId id, CancellationToken cancellationToken = default);

    /// <summary>Loads every aggregate matching a specification.</summary>
    /// <param name="specification">The predicate to apply in the store.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The matching aggregates.</returns>
    Task<IReadOnlyList<TEntity>> ListAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default);

    /// <summary>Counts the aggregates matching a specification.</summary>
    /// <param name="specification">The predicate to apply in the store.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of matches.</returns>
    Task<int> CountAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default);

    /// <summary>Determines whether any aggregate matches a specification.</summary>
    /// <param name="specification">The predicate to apply in the store.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><see langword="true"/> when at least one aggregate matches.</returns>
    Task<bool> AnyAsync(
        ISpecification<TEntity> specification,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Read and write access to an aggregate root.
/// </summary>
/// <typeparam name="TEntity">The aggregate root type.</typeparam>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <remarks>
/// Write methods stage a change; nothing is persisted until <see cref="IUnitOfWork.SaveChangesAsync"/>
/// is called. Keeping the commit boundary out of the repository is what allows a conversion run and
/// its validation report to be written atomically.
/// </remarks>
public interface IRepository<TEntity, in TId> : IReadOnlyRepository<TEntity, TId>
    where TEntity : class, IAggregateRoot
    where TId : notnull
{
    /// <summary>Stages a new aggregate for insertion.</summary>
    /// <param name="entity">The aggregate to add.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the aggregate has been staged.</returns>
    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>Stages an existing aggregate for update.</summary>
    /// <param name="entity">The aggregate to update.</param>
    void Update(TEntity entity);

    /// <summary>Stages an aggregate for deletion.</summary>
    /// <param name="entity">The aggregate to remove.</param>
    void Remove(TEntity entity);
}
