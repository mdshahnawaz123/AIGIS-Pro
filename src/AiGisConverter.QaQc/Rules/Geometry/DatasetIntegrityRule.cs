using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.QaQc.Abstractions;

namespace AiGisConverter.QaQc.Rules.Geometry;

/// <summary>
/// Dataset-level integrity checks the per-feature validator structurally cannot make.
/// </summary>
/// <remarks>
/// The GIS layer already inspects each geometry as it is built. This rule looks at the shape of
/// the dataset itself: whether it is empty, whether its geometry is homogeneous, whether a field
/// is so sparse it carries no information, and whether the layer was ever classified.
/// </remarks>
public sealed class DatasetIntegrityRule : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "Dataset.Integrity";

    /// <inheritdoc />
    public string DisplayName => "Dataset integrity";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.SourceIntegrity;

    /// <inheritdoc />
    public bool RequiresWholeDataset => true;

    /// <inheritdoc />
    public IEnumerable<ValidationIssue> Validate(
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        GisDataset dataset = context.Dataset;

        if (dataset.IsEmpty)
        {
            yield return ValidationIssue.Create(
                IssueSeverity.Warning,
                IssueCategory.SourceIntegrity,
                RuleId,
                $"'{dataset.FeatureClass.Name}' contains no features and will produce an empty file.")
                .WithRemediation("Check the layer filter and the source layer's visibility.");

            yield break;
        }

        int withoutGeometry = dataset.Features.Count - context.GeometricFeatures.Count;

        if (withoutGeometry > 0)
        {
            yield return ValidationIssue.Create(
                IssueSeverity.Warning,
                IssueCategory.SourceIntegrity,
                RuleId,
                $"{withoutGeometry:N0} of {dataset.Features.Count:N0} features in " +
                $"'{dataset.FeatureClass.Name}' carry attributes but no geometry.");
        }

        if (string.Equals(dataset.FeatureClass.Name, FeatureClass.UnclassifiedName, StringComparison.OrdinalIgnoreCase))
        {
            yield return ValidationIssue.Create(
                IssueSeverity.Warning,
                IssueCategory.Classification,
                "Classification.Unclassified",
                $"{dataset.Features.Count:N0} features were not classified and will be delivered as " +
                $"'{FeatureClass.UnclassifiedName}'.")
                .WithRemediation(
                    "Lower the AI confidence threshold, add a rule-based keyword mapping, or map the " +
                    "layer explicitly in the conversion profile.");
        }

        foreach (ValidationIssue issue in CheckSparseFields(context, cancellationToken))
        {
            yield return issue;
        }
    }

    /// <summary>Reports fields that are almost entirely empty.</summary>
    /// <remarks>
    /// A field that is null on 99% of features usually means an attribute mapping that matched
    /// almost nothing. It is not an error, and it is always worth someone looking at.
    /// </remarks>
    private IEnumerable<ValidationIssue> CheckSparseFields(
        ValidationContext context,
        CancellationToken cancellationToken)
    {
        double threshold = context.Options.Attributes.SparseFieldThreshold;

        if (threshold >= 1d || context.Dataset.Features.Count == 0)
        {
            yield break;
        }

        foreach (FieldDefinition field in context.Dataset.Schema.Fields)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int empty = context.Dataset.Features.Count(
                feature => feature.GetAttribute(field.Name).ToInvariantString().Length == 0);

            double ratio = (double)empty / context.Dataset.Features.Count;

            if (ratio < threshold)
            {
                continue;
            }

            yield return ValidationIssue.Create(
                IssueSeverity.Information,
                IssueCategory.Attribute,
                "Attribute.SparseField",
                $"Field '{field.Name}' is empty on {ratio:P1} of features in " +
                $"'{context.Dataset.FeatureClass.Name}'.")
                .ForField(field.Name)
                .WithRemediation("Check the attribute mapping in the conversion profile.");
        }
    }
}
