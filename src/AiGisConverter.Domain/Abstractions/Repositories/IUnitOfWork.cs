using AiGisConverter.Domain.Common;

namespace AiGisConverter.Domain.Abstractions.Repositories;

/// <summary>
/// The transaction boundary. Commits staged changes and dispatches the domain events the
/// aggregates raised while doing so.
/// </summary>
/// <remarks>
/// Events are dispatched <em>after</em> the commit succeeds, never before. An event announcing a
/// finished conversion run that is then rolled back would leave every handler acting on something
/// that did not happen.
/// </remarks>
public interface IUnitOfWork
{
    /// <summary>Persists every staged change and dispatches the resulting domain events.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of state entries written.</returns>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

    /// <summary>Begins an explicit transaction spanning several calls to <see cref="SaveChangesAsync"/>.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The transaction handle.</returns>
    Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>Collects the events raised by every tracked aggregate, without clearing them.</summary>
    /// <returns>The pending events, in the order they were raised.</returns>
    IReadOnlyList<IDomainEvent> CollectPendingEvents();
}

/// <summary>An explicit transaction.</summary>
public interface IUnitOfWorkTransaction : IAsyncDisposable
{
    /// <summary>Commits the transaction.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the transaction is committed.</returns>
    Task CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>Rolls the transaction back.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the transaction is rolled back.</returns>
    Task RollbackAsync(CancellationToken cancellationToken = default);
}
