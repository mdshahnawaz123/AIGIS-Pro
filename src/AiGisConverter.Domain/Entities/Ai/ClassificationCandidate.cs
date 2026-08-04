using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Entities.Ai;

/// <summary>
/// One label proposed for a <see cref="ClassificationSubject"/>, with its score.
/// </summary>
/// <param name="Label">The proposed GIS feature class label.</param>
/// <param name="Confidence">The score assigned to the label.</param>
/// <param name="RuleName">The rule that matched, if any.</param>
/// <param name="Priority">The priority of the rule.</param>
/// <param name="Reason">The reasoning behind the classification.</param>
public sealed record ClassificationCandidate(string Label, Confidence Confidence, string? RuleName = null, int Priority = 0, string? Reason = null);
