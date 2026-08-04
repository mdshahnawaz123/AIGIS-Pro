using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Abstractions;
using NetTopologySuite.Geometries;

namespace AiGisConverter.QaQc.Rules.Topology;

/// <summary>
/// Reports splinter polygons produced by mis-snapped boundaries.
/// </summary>
/// <remarks>
/// <para>
/// Measured by the isoperimetric quotient, <c>4&#960;A / P&#178;</c>: 1 for a circle, 0.785 for a
/// square, tending to zero as a shape becomes a splinter.
/// </para>
/// <para>
/// Thinness alone is not enough. A 100&#160;m by 1&#160;m footpath scores 0.031 and is perfectly
/// legitimate; a 100&#160;m by 0.1&#160;m sliver scores 0.0031 and is not. Requiring the polygon to
/// be small in absolute area as well is what separates a defect from a narrow real feature, and it
/// is why this rule needs two thresholds rather than one.
/// </para>
/// </remarks>
public sealed class SliverPolygonRule : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "Topology.Slivers";

    /// <inheritdoc />
    public string DisplayName => "Sliver polygons";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Topology;

    /// <inheritdoc />
    public bool RequiresWholeDataset => false;

    /// <inheritdoc />
    public IEnumerable<ValidationIssue> Validate(
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        TopologyRuleOptionsView options = new(context);

        if (!options.Enabled || context.Dataset.FeatureClass.Geometry != GeometryKind.Polygon)
        {
            yield break;
        }

        foreach (GisFeature feature in context.GeometricFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double area = feature.Geometry!.Area;
            double perimeter = feature.Geometry.Length;

            if (area <= 0d || perimeter <= 0d || area > options.MaximumArea)
            {
                continue;
            }

            double thinness = 4d * Math.PI * area / (perimeter * perimeter);

            if (thinness > options.ThinnessRatio)
            {
                continue;
            }

            Coordinate? at = feature.Geometry.InteriorPoint?.Coordinate;

            ValidationIssue issue = ValidationIssue.Create(
                IssueSeverity.Warning,
                IssueCategory.Topology,
                RuleId,
                $"Feature '{feature.Id}' is a sliver: area {area:G6}, thinness {thinness:G4} " +
                $"(below {options.ThinnessRatio:G4}).")
                .ForFeature(feature.Id)
                .WithRemediation("Usually a mis-snapped boundary. Snap the neighbouring edges and re-run.");

            yield return at is null ? issue : issue.At(at.X, at.Y);
        }
    }

    /// <summary>Reads the sliver thresholds, keeping the rule body free of option plumbing.</summary>
    private readonly struct TopologyRuleOptionsView
    {
        public TopologyRuleOptionsView(ValidationContext context)
        {
            Enabled = context.Options.Topology.CheckSlivers;
            ThinnessRatio = context.Options.Topology.SliverThinnessRatio;
            MaximumArea = context.Options.Topology.SliverMaximumArea;
        }

        public bool Enabled { get; }

        public double ThinnessRatio { get; }

        public double MaximumArea { get; }
    }
}
