namespace AiGisConverter.Domain.Common;

/// <summary>
/// Base record for domain events. Immutable by construction.
/// </summary>
/// <remarks>
/// Derived events are declared as positional records so they cannot be mutated after they are
/// raised. An event that could be edited between being raised and being handled would be a
/// statement about the past that changes, which is not a useful thing to have.
/// </remarks>
public abstract record DomainEvent : IDomainEvent
{
    /// <summary>Initializes a new instance of the <see cref="DomainEvent"/> class.</summary>
    protected DomainEvent()
    {
        EventId = Guid.NewGuid();
        OccurredAtUtc = DateTimeOffset.UtcNow;
    }

    /// <inheritdoc />
    public Guid EventId { get; init; }

    /// <inheritdoc />
    public DateTimeOffset OccurredAtUtc { get; init; }
}
