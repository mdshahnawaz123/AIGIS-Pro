namespace AiGisConverter.Domain.Entities.Ai;

/// <summary>
/// The classification task definition shared by every provider: what labels are allowed and
/// what domain the drawing belongs to.
/// </summary>
public sealed class ClassificationContext
{
    /// <summary>Initializes a new instance of the <see cref="ClassificationContext"/> class.</summary>
    /// <param name="candidateLabels">The closed set of labels a provider may return.</param>
    /// <exception cref="ArgumentException">No candidate labels were supplied.</exception>
    public ClassificationContext(IReadOnlyList<string> candidateLabels)
    {
        ArgumentNullException.ThrowIfNull(candidateLabels);

        if (candidateLabels.Count == 0)
        {
            throw new ArgumentException("At least one candidate label is required.", nameof(candidateLabels));
        }

        CandidateLabels = candidateLabels;
    }

    /// <summary>Gets the closed set of labels a provider may return.</summary>
    public IReadOnlyList<string> CandidateLabels { get; }

    /// <summary>Gets or sets an optional domain hint, for example "highway design" or "utility network".</summary>
    public string? DomainHint { get; set; }

    /// <summary>Gets or sets the label assigned when no candidate is confident enough.</summary>
    public string UnknownLabel { get; set; } = "Unclassified";

    /// <summary>Gets or sets the drawing units, when known, as a further signal for the model.</summary>
    public string? DrawingUnits { get; set; }
}
