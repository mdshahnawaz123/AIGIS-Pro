using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Abstractions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;

namespace AiGisConverter.QaQc.Rules.Topology;

/// <summary>
/// Reports line endpoints that do not meet any other line.
/// </summary>
/// <remarks>
/// <para>
/// A dangle is an unsnapped end: a pipe that stops a few millimetres short of the junction it is
/// meant to join. The drawing looks connected and the network is not, so every downstream trace
/// &#8212; flow, connectivity, isolation &#8212; is wrong.
/// </para>
/// <para>
/// A genuine terminus is also a dangle by this definition, which is why the finding is a warning
/// rather than an error. Nobody but the surveyor can tell a stub end from a mistake.
/// </para>
/// </remarks>
public sealed class DanglingEndpointRule : IValidationRule
{
    /// <inheritdoc />
    public string RuleId => "Topology.Dangles";

    /// <inheritdoc />
    public string DisplayName => "Unsnapped line endpoints";

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

        if (!context.Options.Topology.CheckDangles || context.Dataset.FeatureClass.Geometry != GeometryKind.Line)
        {
            yield break;
        }

        double tolerance = context.Options.Topology.DangleTolerance;
        STRtree<Endpoint> index = new();
        List<Endpoint> endpoints = [];

        foreach (GisFeature feature in context.GeometricFeatures)
        {
            foreach (Endpoint endpoint in EndpointsOf(feature))
            {
                endpoints.Add(endpoint);
                index.Insert(Envelope(endpoint.Coordinate, tolerance), endpoint);
            }
        }

        if (endpoints.Count == 0)
        {
            yield break;
        }

        index.Build();

        foreach (Endpoint endpoint in endpoints)
        {
            cancellationToken.ThrowIfCancellationRequested();

            bool connected = false;

            foreach (Endpoint other in index.Query(Envelope(endpoint.Coordinate, tolerance)))
            {
                if (!string.Equals(other.FeatureId, endpoint.FeatureId, StringComparison.Ordinal) &&
                    other.Coordinate.Distance(endpoint.Coordinate) <= tolerance)
                {
                    connected = true;
                    break;
                }
            }

            if (connected)
            {
                continue;
            }

            yield return ValidationIssue.Create(
                IssueSeverity.Warning,
                IssueCategory.Topology,
                RuleId,
                $"The {endpoint.Which} endpoint of feature '{endpoint.FeatureId}' meets no other line " +
                $"within {tolerance:G6} units.")
                .ForFeature(endpoint.FeatureId)
                .At(endpoint.Coordinate.X, endpoint.Coordinate.Y)
                .WithRemediation(
                    "Snap the endpoint to the line it should join, or confirm it is a genuine terminus.");
        }
    }

    private static IEnumerable<Endpoint> EndpointsOf(GisFeature feature)
    {
        for (int i = 0; i < feature.Geometry!.NumGeometries; i++)
        {
            if (feature.Geometry.GetGeometryN(i) is not LineString { IsClosed: false } line || line.NumPoints < 2)
            {
                // A closed ring has no free end by definition.
                continue;
            }

            yield return new Endpoint(feature.Id, line.GetCoordinateN(0), "start");
            yield return new Endpoint(feature.Id, line.GetCoordinateN(line.NumPoints - 1), "end");
        }
    }

    private static Envelope Envelope(Coordinate coordinate, double tolerance) =>
        new(coordinate.X - tolerance, coordinate.X + tolerance, coordinate.Y - tolerance, coordinate.Y + tolerance);

    private sealed record Endpoint(string FeatureId, Coordinate Coordinate, string Which);
}
