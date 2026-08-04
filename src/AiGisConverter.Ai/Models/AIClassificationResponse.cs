using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Models;

/// <summary>
/// A provider-agnostic classification response.
/// </summary>
/// <param name="Results">One result per subject the provider was able to classify.</param>
/// <param name="ProviderKey">Key of the provider that produced the response.</param>
/// <param name="ModelIdentifier">The model actually used, when the provider can report one.</param>
/// <param name="Usage">Cost and latency telemetry.</param>
public sealed record AIClassificationResponse(
    IReadOnlyList<ClassificationResult> Results,
    string ProviderKey,
    string? ModelIdentifier,
    AIUsage Usage);
