using AiGisConverter.Data.Abstractions;
using AiGisConverter.Data.Context;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Data.UnitOfWork;

/// <summary>
/// Default <see cref="IUnitOfWork"/>.
/// </summary>
/// <remarks>
/// <para>
/// Events are collected before the save and dispatched after it succeeds. The ordering is the
/// whole point: an event announcing a finished conversion run that is then rolled back would leave
/// every handler acting on something that did not happen.
/// </para>
/// <para>
/// Events are cleared from the aggregates before dispatch, not after. A handler that causes
/// another save must not find the same events waiting to be raised a second time.
/// </para>
/// </remarks>
public sealed class UnitOfWork : IUnitOfWork
{
    private readonly AiGisConverterDbContext _context;
    private readonly IDomainEventDispatcher _dispatcher;
    private readonly ILogger<UnitOfWork> _logger;

    /// <summary>Initializes a new instance of the <see cref="UnitOfWork"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="dispatcher">Delivers domain events after the commit.</param>
    /// <param name="logger">Logger for persistence diagnostics.</param>
    public UnitOfWork(
        AiGisConverterDbContext context,
        IDomainEventDispatcher dispatcher,
        ILogger<UnitOfWork> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IDomainEvent> events = CollectPendingEvents();
        ClearPendingEvents();

        int written = await _context.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (events.Count > 0)
        {
            _logger.LogDebug("Dispatching {EventCount} domain events after committing {Written} rows.",
                events.Count, written);

            await _dispatcher.DispatchAsync(events, cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    /// <inheritdoc />
    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        IDbContextTransaction transaction =
            await _context.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);

        return new EfUnitOfWorkTransaction(transaction);
    }

    /// <inheritdoc />
    public IReadOnlyList<IDomainEvent> CollectPendingEvents() =>
        [.. _context.ChangeTracker
            .Entries<IAggregateRoot>()
            .SelectMany(entry => entry.Entity.DomainEvents)];

    private void ClearPendingEvents()
    {
        foreach (var entry in _context.ChangeTracker.Entries<IAggregateRoot>())
        {
            entry.Entity.ClearDomainEvents();
        }
    }

    /// <summary>Wraps an Entity Framework transaction in the domain's contract.</summary>
    private sealed class EfUnitOfWorkTransaction : IUnitOfWorkTransaction
    {
        private readonly IDbContextTransaction _transaction;

        public EfUnitOfWorkTransaction(IDbContextTransaction transaction) => _transaction = transaction;

        public Task CommitAsync(CancellationToken cancellationToken = default) =>
            _transaction.CommitAsync(cancellationToken);

        public Task RollbackAsync(CancellationToken cancellationToken = default) =>
            _transaction.RollbackAsync(cancellationToken);

        public ValueTask DisposeAsync() => _transaction.DisposeAsync();
    }
}
