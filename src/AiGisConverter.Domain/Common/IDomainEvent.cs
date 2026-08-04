namespace AiGisConverter.Domain.Common;

/// <summary>
/// Something that has happened in the domain and that other parts of the system may care about.
/// </summary>
/// <remarks>
/// Domain events are raised by aggregates and dispatched after the unit of work commits, never
/// during. An event describes the past: it is named in the past tense, it is immutable, and
/// handling it must not be able to change whether the thing happened.
/// </remarks>
public interface IDomainEvent
{
    /// <summary>Gets the unique identifier of this event occurrence.</summary>
    Guid EventId { get; }

    /// <summary>Gets the instant, in UTC, at which the event occurred.</summary>
    DateTimeOffset OccurredAtUtc { get; }
}
