using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.QaQc.Abstractions;

namespace AiGisConverter.QaQc.Rules.Attribute;

/// <summary>Reports features missing a value the delivery specification demands.</summary>
public sealed class RequiredFieldRule : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "Attribute.RequiredField";

    /// <inheritdoc />
    public string DisplayName => "Required field missing";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Attribute;

    /// <inheritdoc />
    public bool RequiresWholeDataset => false;

    /// <inheritdoc />
    public IEnumerable<ValidationIssue> Validate(
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        IList<string> required = context.Options.Attributes.RequiredFields;

        if (required.Count == 0)
        {
            yield break;
        }

        // A field absent from the schema is one finding for the dataset; a field present but empty
        // is one finding per feature. Conflating them buries the schema problem under the rows.
        foreach (string field in required.Where(f => !context.Dataset.Schema.Contains(f)))
        {
            yield return ValidationIssue.Create(
                IssueSeverity.Error,
                IssueCategory.Attribute,
                RuleId,
                $"Required field '{field}' is not present in the schema of '{context.Dataset.FeatureClass.Name}'.")
                .ForField(field)
                .WithRemediation("Map a source attribute onto this field in the conversion profile.");
        }

        foreach (GisFeature feature in context.Dataset.Features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (string field in required.Where(f => context.Dataset.Schema.Contains(f)))
            {
                AttributeValue value = feature.GetAttribute(field);

                if (!value.IsNull && value.ToInvariantString().Length > 0)
                {
                    continue;
                }

                yield return ValidationIssue.Create(
                    IssueSeverity.Error,
                    IssueCategory.Attribute,
                    RuleId,
                    $"Feature '{feature.Id}' has no value for required field '{field}'.")
                    .ForFeature(feature.Id)
                    .ForField(field);
            }
        }
    }
}

/// <summary>Reports duplicate values in a field declared unique.</summary>
/// <remarks>
/// A duplicated identifier breaks every join a recipient will attempt, and it is invisible until
/// they attempt one.
/// </remarks>
public sealed class UniqueFieldRule : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "Attribute.DuplicateValue";

    /// <inheritdoc />
    public string DisplayName => "Duplicate value in a unique field";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Attribute;

    /// <inheritdoc />
    public bool RequiresWholeDataset => true;

    /// <inheritdoc />
    public IEnumerable<ValidationIssue> Validate(
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (string field in context.Options.Attributes.UniqueFields)
        {
            if (!context.Dataset.Schema.Contains(field))
            {
                continue;
            }

            Dictionary<string, string> firstSeen = new(StringComparer.Ordinal);

            foreach (GisFeature feature in context.Dataset.Features)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string value = feature.GetAttribute(field).ToInvariantString();

                if (value.Length == 0)
                {
                    continue;
                }

                if (firstSeen.TryGetValue(value, out string? owner))
                {
                    yield return ValidationIssue.Create(
                        IssueSeverity.Error,
                        IssueCategory.Attribute,
                        RuleId,
                        $"Field '{field}' value '{value}' appears on both '{owner}' and '{feature.Id}'.")
                        .ForFeature(feature.Id)
                        .ForField(field)
                        .WithRemediation("Renumber one of the features, or drop the uniqueness requirement.");
                }
                else
                {
                    firstSeen[value] = feature.Id;
                }
            }
        }
    }
}
