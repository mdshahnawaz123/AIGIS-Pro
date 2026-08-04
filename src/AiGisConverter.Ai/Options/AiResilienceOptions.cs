using System.ComponentModel.DataAnnotations;

namespace AiGisConverter.Ai.Options;

/// <summary>
/// Retry and timeout policy applied uniformly to every provider by the resilience decorator.
/// </summary>
public sealed class AiResilienceOptions
{
    /// <summary>Gets or sets the number of retries after the first attempt.</summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 2;

    /// <summary>Gets or sets the base delay in milliseconds for exponential backoff.</summary>
    [Range(0, 60000)]
    public int BaseDelayMilliseconds { get; set; } = 500;

    /// <summary>Gets or sets the per-attempt timeout in seconds.</summary>
    [Range(1, 3600)]
    public int PerAttemptTimeoutSeconds { get; set; } = 120;
}
