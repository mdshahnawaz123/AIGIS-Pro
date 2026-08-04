using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.Entities.Semantic;

/// <summary>
/// Defines a directed relationship between two semantic features.
/// </summary>
public sealed class SemanticRelationship
{
    /// <summary>
    /// Gets the type of relationship.
    /// </summary>
    public SemanticRelationshipType RelationshipType { get; }

    /// <summary>
    /// Gets the identifier of the source feature.
    /// </summary>
    public string SourceFeatureId { get; }

    /// <summary>
    /// Gets the identifier of the target feature.
    /// </summary>
    public string TargetFeatureId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticRelationship"/> class.
    /// </summary>
    public SemanticRelationship(SemanticRelationshipType type, string sourceFeatureId, string targetFeatureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFeatureId);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetFeatureId);

        RelationshipType = type;
        SourceFeatureId = sourceFeatureId;
        TargetFeatureId = targetFeatureId;
    }

    /// <inheritdoc />
    public override string ToString() => $"{SourceFeatureId} {RelationshipType} {TargetFeatureId}";
}
