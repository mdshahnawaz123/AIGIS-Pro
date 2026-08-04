using AiGisConverter.Domain.Entities.Source;
using System.Collections.Generic;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Evaluates mapping profiles to determine the classification of a source element based on configurable rules.
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Evaluates the rules and returns all matching candidates sorted by priority and confidence.
    /// </summary>
    IReadOnlyList<AiGisConverter.Domain.Entities.Ai.ClassificationCandidate> Evaluate(SourceElement element);

    /// <summary>
    /// Evaluates the rules against a semantic feature.
    /// </summary>
    IReadOnlyList<AiGisConverter.Domain.Entities.Ai.ClassificationCandidate> Evaluate(AiGisConverter.Domain.Entities.Semantic.SemanticFeature feature);
}
