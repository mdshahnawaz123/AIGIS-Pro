using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// Parses the free-form text a language model returns into strongly typed results, tolerating
/// the usual deviations: markdown fences, prose preamble, unknown labels, out-of-range scores.
/// </summary>
public interface IClassificationResponseParser
{
    /// <summary>Parses a model response.</summary>
    /// <param name="content">Raw assistant content.</param>
    /// <param name="subjects">The subjects that were asked about, used to correlate and validate.</param>
    /// <param name="context">The task context, used to reject labels outside the candidate set.</param>
    /// <param name="providerKey">Key of the provider that produced the content.</param>
    /// <returns>One result per subject the model answered for. Unanswered subjects are omitted.</returns>
    IReadOnlyList<ClassificationResult> Parse(
        string content,
        IReadOnlyList<ClassificationSubject> subjects,
        ClassificationContext context,
        string providerKey);
}
