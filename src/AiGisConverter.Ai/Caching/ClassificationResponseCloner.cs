using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Caching;

/// <summary>
/// Produces independent copies of a classification response.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ClassificationResult"/> is mutable by design: the classification service stamps
/// acceptance onto it once the confidence threshold is known. That is fine for an object with a
/// single owner and wrong for one held in a shared cache, because two callers with different
/// thresholds would then be writing to the same field.
/// </para>
/// <para>
/// Cloning at the cache boundary keeps both properties: the domain entity stays as it is, and no
/// caller can reach an instance another caller also holds. The alternative &#8212; making the
/// entity immutable &#8212; would change a public contract in a frozen module to solve a problem
/// that belongs to the cache.
/// </para>
/// <para>
/// The cost is one small object per result per cache operation, against an inference call the
/// cache exists to avoid. It is not a trade worth thinking about.
/// </para>
/// </remarks>
public static class ClassificationResponseCloner
{
    /// <summary>Copies a response and every result inside it.</summary>
    /// <param name="response">The response to copy.</param>
    /// <returns>A copy sharing no mutable state with the original.</returns>
    public static AIClassificationResponse Clone(AIClassificationResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);

        List<ClassificationResult> copies = new(response.Results.Count);

        foreach (ClassificationResult result in response.Results)
        {
            copies.Add(Clone(result));
        }

        // The record itself is copied so that a caller replacing Results on their instance cannot
        // reach the cached one. Usage and metadata are immutable value types and need no copy.
        return response with { Results = copies };
    }

    /// <summary>Copies a single result.</summary>
    /// <param name="result">The result to copy.</param>
    /// <returns>A copy sharing no mutable state with the original.</returns>
    public static ClassificationResult Clone(ClassificationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        ClassificationResult copy = new(result.SubjectId, result.Label, result.Confidence, result.ProviderKey)
        {
            Rationale = result.Rationale,
        };

        copy.MarkAccepted(result.IsAccepted);

        foreach (ClassificationCandidate alternative in result.Alternatives)
        {
            // ClassificationCandidate is an immutable record, so the reference may be shared.
            copy.AddAlternative(alternative);
        }

        return copy;
    }

    /// <summary>Copies a result and stamps a new acceptance onto the copy.</summary>
    /// <param name="result">The result to copy.</param>
    /// <param name="threshold">The confidence threshold to apply.</param>
    /// <returns>A copy carrying the acceptance decision for this caller's threshold.</returns>
    public static ClassificationResult CloneWithThreshold(ClassificationResult result, double threshold)
    {
        ClassificationResult copy = Clone(result);
        copy.ApplyThreshold(threshold);

        return copy;
    }
}
