namespace AiGisConverter.Ai.Models;

/// <summary>
/// Optional cost and latency telemetry reported by a provider.
/// </summary>
/// <param name="PromptTokens">Tokens consumed by the input, when the provider reports them.</param>
/// <param name="CompletionTokens">Tokens produced, when the provider reports them.</param>
/// <param name="Duration">Wall-clock duration of the inference call.</param>
public sealed record AIUsage(int? PromptTokens, int? CompletionTokens, TimeSpan Duration)
{
    /// <summary>An empty usage record for providers that report no telemetry.</summary>
    public static readonly AIUsage Empty = new(null, null, TimeSpan.Zero);
}
