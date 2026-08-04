using AiGisConverter.Domain.Abstractions.Services;

namespace AiGisConverter.Infrastructure.Time;

/// <summary>The real clock.</summary>
/// <remarks>
/// Trivial, and the only implementation that will ever exist in production. It is here so that
/// retention windows and schedule decisions can be tested by stating what "now" is rather than by
/// waiting for it.
/// </remarks>
public sealed class SystemClock : IClock
{
    /// <inheritdoc />
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
