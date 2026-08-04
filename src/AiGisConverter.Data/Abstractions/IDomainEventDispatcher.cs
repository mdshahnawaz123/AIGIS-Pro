using AiGisConverter.Domain.Common;

namespace AiGisConverter.Data.Abstractions;

/// <summary>
/// Delivers domain events after a unit of work has committed.
/// </summary>
/// <remarks>
/// <para>
/// Declared here rather than in Domain because dispatch is a persistence concern: it is the commit
/// that decides whether the events describe something that happened. The frozen dependency rule
/// puts Infrastructure downstream of Data, so a dispatcher defined there would be unreachable from
/// the unit of work that needs it.
/// </para>
/// <para>
/// The default implementation does nothing. Handlers are wired by the composition root, which is
/// the only place that knows both the events and the things that care about them.
/// </para>
/// </remarks>
public interface IDomainEventDispatcher
{
    /// <summary>Delivers a batch of events.</summary>
    /// <param name="domainEvents">The events to deliver, in the order they were raised.</param>
    /// <param name="cancellationToken">Token used to cancel delivery.</param>
    /// <returns>A task that completes when every handler has run.</returns>
    Task DispatchAsync(IReadOnlyList<IDomainEvent> domainEvents, CancellationToken cancellationToken = default);
}

/// <summary>A dispatcher that discards events. The default when nothing handles them.</summary>
public sealed class NullDomainEventDispatcher : IDomainEventDispatcher
{
    /// <inheritdoc />
    public Task DispatchAsync(
        IReadOnlyList<IDomainEvent> domainEvents,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}
