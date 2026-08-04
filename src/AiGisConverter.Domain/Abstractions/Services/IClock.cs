namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// The current time, as a dependency.
/// </summary>
/// <remarks>
/// Entities call <see cref="DateTimeOffset.UtcNow"/> directly for their own timestamps, which is
/// pragmatic. This abstraction exists for the places where time is a decision input rather than a
/// record &#8212; retention cut-offs, schedule windows, expiry &#8212; where a test needs to be
/// able to state what "now" is instead of waiting for it.
/// </remarks>
public interface IClock
{
    /// <summary>Gets the current instant in UTC.</summary>
    DateTimeOffset UtcNow { get; }
}
