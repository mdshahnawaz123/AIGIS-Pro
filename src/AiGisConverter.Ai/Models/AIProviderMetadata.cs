namespace AiGisConverter.Ai.Models;

/// <summary>
/// Static, configuration-independent description of a provider. Surfaced in the UI provider
/// picker and used by the orchestration layer to size batches and choose fallbacks.
/// </summary>
/// <param name="Key">Unique, lower-case provider key, for example <c>ollama</c>. Matches <c>Ai:ActiveProvider</c>.</param>
/// <param name="DisplayName">Human-readable name shown in the user interface.</param>
/// <param name="Kind">Execution model of the provider.</param>
/// <param name="MaxSubjectsPerCall">Maximum subjects the provider accepts in a single call.</param>
/// <param name="SupportsRationale">Whether the provider can explain its choice.</param>
/// <param name="RequiresNetwork">Whether the provider needs network access to function.</param>
public sealed record AIProviderMetadata(
    string Key,
    string DisplayName,
    AIProviderKind Kind,
    int MaxSubjectsPerCall,
    bool SupportsRationale,
    bool RequiresNetwork);
