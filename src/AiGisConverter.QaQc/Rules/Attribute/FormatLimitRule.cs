using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.QaQc.Abstractions;

namespace AiGisConverter.QaQc.Rules.Attribute;

/// <summary>
/// Reports schema and values that the target format will silently mangle.
/// </summary>
/// <remarks>
/// Shapefile's DBF header caps field names at ten characters and text values at 254. Neither limit
/// produces an error on write: the driver truncates. A field name collision after truncation
/// silently merges two columns, which is the worst of the three outcomes and the hardest to spot.
/// </remarks>
public sealed class FormatLimitRule : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "Export.FormatLimit";

    /// <inheritdoc />
    public string DisplayName => "Target format limits";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Export;

    /// <inheritdoc />
    public bool RequiresWholeDataset => false;

    /// <inheritdoc />
    public IEnumerable<ValidationIssue> Validate(
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        int nameLimit = context.Options.Attributes.MaximumFieldNameLength;
        int textLimit = context.Options.Attributes.MaximumTextLength;

        if (nameLimit > 0)
        {
            foreach (ValidationIssue issue in CheckFieldNames(context, nameLimit))
            {
                yield return issue;
            }
        }

        if (textLimit <= 0)
        {
            yield break;
        }

        foreach (GisFeature feature in context.Dataset.Features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (FieldDefinition field in context.Dataset.Schema.Fields
                .Where(static f => f.DataType == AttributeDataType.Text))
            {
                string value = feature.GetAttribute(field.Name).ToInvariantString();

                if (value.Length <= textLimit)
                {
                    continue;
                }

                yield return ValidationIssue.Create(
                    IssueSeverity.Warning,
                    IssueCategory.Export,
                    RuleId,
                    $"Feature '{feature.Id}' field '{field.Name}' is {value.Length} characters; " +
                    $"the target format truncates at {textLimit}.")
                    .ForFeature(feature.Id)
                    .ForField(field.Name)
                    .WithRemediation("Shorten the value, or deliver in GeoPackage, which has no such limit.");
            }
        }
    }

    private IEnumerable<ValidationIssue> CheckFieldNames(ValidationContext context, int limit)
    {
        Dictionary<string, string> truncated = new(StringComparer.OrdinalIgnoreCase);

        foreach (FieldDefinition field in context.Dataset.Schema.Fields)
        {
            if (field.Name.Length > limit)
            {
                yield return ValidationIssue.Create(
                    IssueSeverity.Warning,
                    IssueCategory.Export,
                    RuleId,
                    $"Field name '{field.Name}' is {field.Name.Length} characters; " +
                    $"the target format truncates at {limit}.")
                    .ForField(field.Name);
            }

            string key = field.Name.Length > limit ? field.Name[..limit] : field.Name;

            if (truncated.TryGetValue(key, out string? other))
            {
                // Two columns becoming one is data loss with no error message anywhere.
                yield return ValidationIssue.Create(
                    IssueSeverity.Error,
                    IssueCategory.Export,
                    RuleId,
                    $"Fields '{other}' and '{field.Name}' both truncate to '{key}' and would be merged.")
                    .ForField(field.Name)
                    .WithRemediation("Rename one field in the conversion profile's attribute mapping.");
            }
            else
            {
                truncated[key] = field.Name;
            }
        }
    }
}
