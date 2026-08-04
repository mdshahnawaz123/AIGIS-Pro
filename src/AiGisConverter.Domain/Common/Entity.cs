namespace AiGisConverter.Domain.Common;

/// <summary>
/// Base class for entities: objects whose identity, not their attribute values, defines equality.
/// </summary>
/// <typeparam name="TId">The identifier type.</typeparam>
/// <remarks>
/// Two conversion runs with identical timings and counts are still two different runs. That is the
/// whole distinction between an entity and a value object, and it is why equality here is defined
/// on <see cref="Id"/> alone.
/// </remarks>
public abstract class Entity<TId> : IEquatable<Entity<TId>>
    where TId : notnull
{
    private readonly List<IDomainEvent> _domainEvents = [];

    /// <summary>Initializes a new instance of the <see cref="Entity{TId}"/> class.</summary>
    /// <param name="id">The entity identifier.</param>
    protected Entity(TId id)
    {
        ArgumentNullException.ThrowIfNull(id);
        Id = id;
    }

    /// <summary>Gets the entity identifier.</summary>
    public TId Id { get; }

    /// <summary>Gets the events raised by this entity and not yet dispatched.</summary>
    public IReadOnlyCollection<IDomainEvent> DomainEvents => _domainEvents;

    /// <summary>Records a domain event for dispatch after the unit of work commits.</summary>
    /// <param name="domainEvent">The event to record.</param>
    protected void Raise(IDomainEvent domainEvent)
    {
        ArgumentNullException.ThrowIfNull(domainEvent);
        _domainEvents.Add(domainEvent);
    }

    /// <summary>Clears the pending events.</summary>
    public void ClearDomainEvents() => _domainEvents.Clear();

    /// <inheritdoc />
    public bool Equals(Entity<TId>? other) =>
        other is not null && GetType() == other.GetType() && EqualityComparer<TId>.Default.Equals(Id, other.Id);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is Entity<TId> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(GetType(), Id);

    /// <summary>Determines whether two entities are the same entity.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when both are null or have the same type and identifier.</returns>
    public static bool operator ==(Entity<TId>? left, Entity<TId>? right) =>
        left is null ? right is null : left.Equals(right);

    /// <summary>Determines whether two entities are different entities.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when the entities differ.</returns>
    public static bool operator !=(Entity<TId>? left, Entity<TId>? right) => !(left == right);

    /// <inheritdoc />
    public override string ToString() => $"{GetType().Name}({Id})";
}
