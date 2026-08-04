using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Abstractions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace AiGisConverter.QaQc.Rules.Topology;

/// <summary>
/// Reports polygons that overlap each other.
/// </summary>
/// <remarks>
/// <para>
/// The defect that gets cadastral deliveries rejected: two parcels claiming the same ground.
/// It arises from digitising each boundary independently, and it is invisible on screen because
/// the overlap is usually a few centimetres.
/// </para>
/// <para>
/// Candidate pairs come from an R-tree, then the exact predicate runs on the survivors. Each pair
/// is tested once &#8212; the index yields both orderings, and reporting a defect twice makes the
/// report look twice as bad as the data is.
/// </para>
/// </remarks>
public sealed class OverlappingFeaturesRule : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "Topology.Overlaps";

    /// <inheritdoc />
    public string DisplayName => "Overlapping features";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Topology;

    /// <inheritdoc />
    public bool RequiresWholeDataset => true;

    /// <inheritdoc />
    public IEnumerable<ValidationIssue> Validate(
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (!context.Options.Topology.CheckOverlaps || context.Dataset.FeatureClass.Geometry != GeometryKind.Polygon)
        {
            yield break;
        }

        STRtree<GisFeature> index = new();

        foreach (GisFeature feature in context.GeometricFeatures)
        {
            index.Insert(feature.Geometry!.EnvelopeInternal, feature);
        }

        index.Build();

        HashSet<string> seenPairs = new(StringComparer.Ordinal);
        double minimumArea = context.Options.Topology.MinimumOverlapArea;

        foreach (GisFeature feature in context.GeometricFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            foreach (GisFeature candidate in index.Query(feature.Geometry!.EnvelopeInternal))
            {
                if (ReferenceEquals(feature, candidate) ||
                    string.Equals(feature.Id, candidate.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                // Order the pair so A-B and B-A are the same key.
                string pairKey = string.CompareOrdinal(feature.Id, candidate.Id) < 0
                    ? $"{feature.Id}|{candidate.Id}"
                    : $"{candidate.Id}|{feature.Id}";

                if (!seenPairs.Add(pairKey))
                {
                    continue;
                }

                NetTopologySuite.Geometries.Geometry? shared = TryIntersection(feature, candidate);

                if (shared is null || shared.Area <= minimumArea)
                {
                    continue;
                }

                Coordinate? at = shared.InteriorPoint?.Coordinate;

                ValidationIssue issue = ValidationIssue.Create(
                    IssueSeverity.Error,
                    IssueCategory.Topology,
                    RuleId,
                    $"Feature '{feature.Id}' overlaps '{candidate.Id}' by {shared.Area:G6} square units.")
                    .ForFeature(feature.Id)
                    .WithRemediation("Snap the shared boundary, or clip one feature to the other.");

                yield return at is null ? issue : issue.At(at.X, at.Y);
            }
        }
    }

    /// <summary>Intersects two features, treating a topology failure as "no reportable overlap".</summary>
    /// <remarks>
    /// An overlay that cannot be computed is a geometry defect, and the per-feature geometry rules
    /// have already reported it. Raising a second finding here would double-count one problem.
    /// </remarks>
    private static NetTopologySuite.Geometries.Geometry? TryIntersection(GisFeature left, GisFeature right)
    {
        try
        {
            return left.Geometry!.Overlaps(right.Geometry!) ? left.Geometry!.Intersection(right.Geometry!) : null;
        }
        catch (TopologyException)
        {
            return null;
        }
    }
}
