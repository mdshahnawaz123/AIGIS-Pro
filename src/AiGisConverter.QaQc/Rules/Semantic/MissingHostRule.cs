using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Abstractions;

namespace AiGisConverter.QaQc.Rules.Semantic;

/// <summary>
/// A semantic rule that verifies features which require a host actually have one.
/// (e.g. Doors must be hosted by walls).
/// </summary>
public sealed class MissingHostRule : IValidationRule
{
    /// <inheritdoc/>
    public string RuleId => "Semantic.MissingHost";
    
    /// <inheritdoc/>
    public string DisplayName => "Missing Semantic Host";
    
    /// <inheritdoc/>
    public IssueCategory Category => IssueCategory.Attribute; // Close enough to Attribute/Topology
    
    /// <inheritdoc/>
    public bool RequiresWholeDataset => true;

    /// <inheritdoc/>
    public IEnumerable<ValidationIssue> Validate(ValidationContext context, CancellationToken cancellationToken = default)
    {
        // Example: Only apply to doors or windows
        var hostedFeatures = context.Dataset.Features
            .Where(f => f.SemanticFeature?.Category == "Door" || f.SemanticFeature?.Category == "Window" || f.SemanticFeature?.Category == "IfcDoor" || f.SemanticFeature?.Category == "IfcWindow");

        foreach (var feature in hostedFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (feature.SemanticFeature == null)
            {
                continue;
            }

            bool hasHost = feature.SemanticFeature.Relationships.Any(r => r.RelationshipType == AiGisConverter.Domain.Enums.SemanticRelationshipType.BelongsTo);
            
            if (!hasHost)
            {
                yield return ValidationIssue.Create(
                    IssueSeverity.Warning,
                    Category,
                    RuleId,
                    $"The semantic feature '{feature.Id}' (Category: {feature.SemanticFeature.Category}) is not hosted by any element.")
                    .ForFeature(feature.Id);
            }
        }
    }
}
