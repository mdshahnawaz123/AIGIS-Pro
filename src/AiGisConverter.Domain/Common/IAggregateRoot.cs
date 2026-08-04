namespace AiGisConverter.Domain.Common;

/// <summary>
/// Marks the one entity in a cluster through which the cluster is loaded, saved and modified.
/// </summary>
/// <remarks>
/// Repositories exist only for aggregate roots. Entities inside an aggregate &#8212; a
/// <c>ConversionJob</c> within a <c>ConversionProject</c>, for instance &#8212; are reached
/// through their root, which is what keeps the root's invariants enforceable.
/// </remarks>
public interface IAggregateRoot
{
    /// <summary>Gets the events raised by this aggregate and not yet dispatched.</summary>
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    /// <summary>Clears the pending events. Called by the unit of work after dispatch.</summary>
    void ClearDomainEvents();
}
