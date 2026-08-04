using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Entities.Ai;

/// <summary>
/// The outcome of classifying a single <see cref="ClassificationSubject"/>.
/// </summary>
/// <remarks>
/// Acceptance is applied through <see cref="MarkAccepted"/> rather than exposed as a setter,
/// because whether a result is accepted is a decision made once, against the configured threshold,
/// by the classification service. Leaving it writable would let any later stage quietly promote a
/// low-confidence guess into an accepted classification.
/// </remarks>
public sealed class ClassificationResult
{
    private readonly List<ClassificationCandidate> _alternatives = [];

    /// <summary>Initializes a new instance of the <see cref="ClassificationResult"/> class.</summary>
    /// <param name="subjectId">Identifier of the classified subject.</param>
    /// <param name="label">The winning label.</param>
    /// <param name="confidence">Confidence in the winning label.</param>
    /// <param name="providerKey">Key of the provider that produced the result.</param>
    public ClassificationResult(string subjectId, string label, Confidence confidence, string providerKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        SubjectId = subjectId;
        Label = label;
        Confidence = confidence;
        ProviderKey = providerKey;
    }

    /// <summary>Gets the identifier of the classified subject.</summary>
    public string SubjectId { get; }

    /// <summary>Gets the winning label.</summary>
    public string Label { get; }

    /// <summary>Gets the confidence in the winning label.</summary>
    public Confidence Confidence { get; }

    /// <summary>Gets the key of the provider that produced this result.</summary>
    public string ProviderKey { get; }

    /// <summary>Gets the model's stated reasoning, when the provider supplies one.</summary>
    public string? Rationale { get; init; }

    /// <summary>Gets the matched rule name, if any.</summary>
    public string? RuleName { get; init; }

    /// <summary>Gets the confidence level of this classification result.</summary>
    public AiGisConverter.Domain.Enums.ConfidenceLevel Level
    {
        get
        {
            double c = Confidence.Value;
            if (c >= 0.95) 
            {
                return AiGisConverter.Domain.Enums.ConfidenceLevel.Automatic;
            }
            if (c >= 0.80) 
            {
                return AiGisConverter.Domain.Enums.ConfidenceLevel.Review;
            }
            if (c >= 0.60) 
            {
                return AiGisConverter.Domain.Enums.ConfidenceLevel.NeedsAttention;
            }
            return AiGisConverter.Domain.Enums.ConfidenceLevel.Unclassified;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the result met the configured confidence threshold.
    /// Results below the threshold are retained for review rather than discarded.
    /// </summary>
    public bool IsAccepted { get; private set; }

    /// <summary>Gets the runner-up labels, ordered by descending confidence.</summary>
    public IReadOnlyList<ClassificationCandidate> Alternatives => _alternatives;

    /// <summary>Records whether the result met the confidence threshold.</summary>
    /// <param name="accepted">Whether the label is accepted without review.</param>
    public void MarkAccepted(bool accepted) => IsAccepted = accepted;

    /// <summary>Applies a confidence threshold and returns the resulting acceptance.</summary>
    /// <param name="threshold">The minimum confidence for acceptance.</param>
    /// <returns><see langword="true"/> when the result was accepted.</returns>
    public bool ApplyThreshold(double threshold)
    {
        IsAccepted = Confidence.Value >= threshold;

        return IsAccepted;
    }

    /// <summary>Adds a runner-up label.</summary>
    /// <param name="candidate">The alternative to add.</param>
    public void AddAlternative(ClassificationCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        _alternatives.Add(candidate);
    }

    /// <inheritdoc />
    public override string ToString() => $"{SubjectId} -> {Label} ({Confidence})";
}
