using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using AiGisConverter.Gis.Profiles;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Valid;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Geometry;

/// <summary>
/// Default <see cref="IGeometryValidator"/>.
/// </summary>
/// <remarks>
/// <para>
/// Findings are attributed to a location wherever one can be determined, because "polygon 4,812 is
/// self-intersecting" is not actionable and "self-intersection at 528431.2, 181997.6" is.
/// </para>
/// <para>
/// Severity reflects consequence, not how hard the problem was to detect. A self-intersecting ring
/// is an <see cref="IssueSeverity.Error"/> because every area computed from it is wrong; a
/// duplicate vertex is a <see cref="IssueSeverity.Warning"/> because nothing downstream is harmed
/// by it.
/// </para>
/// </remarks>
public sealed class GeometryValidator : IGeometryValidator
{
    private readonly IOptionsMonitor<GisOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="GeometryValidator"/> class.</summary>
    /// <param name="options">Live GIS settings supplying the tolerances.</param>
    public GeometryValidator(IOptionsMonitor<GisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public IReadOnlyList<ValidationIssue> Validate(NtsGeometry? geometry, string featureId, QualityRules rules)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);
        ArgumentNullException.ThrowIfNull(rules);

        GeometryOptions thresholds = _options.CurrentValue.Geometry;
        List<ValidationIssue> issues = [];

        if (geometry is null)
        {
            if (rules.CheckNullGeometry)
            {
                issues.Add(Issue(
                    IssueSeverity.Error,
                    IssueCategory.Geometry,
                    "Geometry.Null",
                    "The feature has no geometry.",
                    featureId));
            }

            return issues;
        }

        if (geometry.IsEmpty)
        {
            issues.Add(Issue(
                IssueSeverity.Warning,
                IssueCategory.Geometry,
                "Geometry.Empty",
                $"The {geometry.GeometryType} is empty.",
                featureId));

            return issues;
        }

        Inspect(geometry, featureId, rules, thresholds, issues);

        return issues;
    }

    private void Inspect(
        NtsGeometry geometry,
        string featureId,
        QualityRules rules,
        GeometryOptions thresholds,
        List<ValidationIssue> issues)
    {
        switch (geometry)
        {
            case GeometryCollection collection and not (MultiPoint or MultiLineString or MultiPolygon):
                foreach (NtsGeometry part in collection.Geometries)
                {
                    Inspect(part, featureId, rules, thresholds, issues);
                }

                return;

            case LineString line:
                InspectLine(line, featureId, rules, thresholds, issues);
                break;

            case Polygon polygon:
                InspectPolygon(polygon, featureId, rules, thresholds, issues);
                break;

            case MultiLineString or MultiPolygon or MultiPoint:
                for (int i = 0; i < geometry.NumGeometries; i++)
                {
                    Inspect(geometry.GetGeometryN(i), featureId, rules, thresholds, issues);
                }

                break;
        }

        if (rules.CheckDuplicateVertices)
        {
            CheckDuplicateVertices(geometry, featureId, thresholds, issues);
        }
    }

    private static void InspectLine(
        LineString line,
        string featureId,
        QualityRules rules,
        GeometryOptions thresholds,
        List<ValidationIssue> issues)
    {
        if (rules.CheckZeroLength && line.Length <= thresholds.MinimumLineLength)
        {
            issues.Add(Issue(
                IssueSeverity.Error,
                IssueCategory.Geometry,
                "Geometry.ZeroLength",
                $"The line is {line.Length:G6} long, at or below the minimum of {thresholds.MinimumLineLength:G6}.",
                featureId,
                line.Coordinate));
        }

        if (rules.CheckSelfIntersection && !line.IsSimple)
        {
            issues.Add(Issue(
                IssueSeverity.Warning,
                IssueCategory.Geometry,
                "Geometry.SelfIntersectingLine",
                "The line crosses itself. This is legal but often indicates a digitising error.",
                featureId,
                line.Coordinate));
        }
    }

    private static void InspectPolygon(
        Polygon polygon,
        string featureId,
        QualityRules rules,
        GeometryOptions thresholds,
        List<ValidationIssue> issues)
    {
        if (rules.CheckZeroArea && polygon.Area <= thresholds.MinimumPolygonArea)
        {
            issues.Add(Issue(
                IssueSeverity.Error,
                IssueCategory.Geometry,
                "Geometry.ZeroArea",
                $"The polygon encloses {polygon.Area:G6}, at or below the minimum of {thresholds.MinimumPolygonArea:G6}.",
                featureId,
                polygon.Coordinate));
        }

        if (rules.CheckRingValidity)
        {
            // IsValidOp names the failure and locates it, which a bare IsValid check cannot.
            IsValidOp validity = new(polygon);

            if (!validity.IsValid)
            {
                TopologyValidationError? error = validity.ValidationError;

                issues.Add(Issue(
                    Severity(error),
                    IssueCategory.Geometry,
                    $"Geometry.{error?.ErrorType.ToString() ?? "Invalid"}",
                    error?.Message ?? "The polygon is topologically invalid.",
                    featureId,
                    error?.Coordinate));
            }

            if (polygon.ExteriorRing.NumPoints < 4)
            {
                issues.Add(Issue(
                    IssueSeverity.Error,
                    IssueCategory.Geometry,
                    "Geometry.DegenerateRing",
                    $"The exterior ring has {polygon.ExteriorRing.NumPoints} points; a ring needs at least four.",
                    featureId,
                    polygon.Coordinate));
            }
        }
    }

    /// <summary>
    /// Flags vertices that repeat within the tolerance.
    /// </summary>
    /// <remarks>
    /// Reported once per geometry rather than once per occurrence. A polyline exported from a badly
    /// configured CAD system can repeat every vertex, and a finding per vertex would bury every
    /// other problem in the report.
    /// </remarks>
    private static void CheckDuplicateVertices(
        NtsGeometry geometry,
        string featureId,
        GeometryOptions thresholds,
        List<ValidationIssue> issues)
    {
        Coordinate[] coordinates = geometry.Coordinates;
        int duplicates = 0;
        Coordinate? first = null;

        for (int i = 1; i < coordinates.Length; i++)
        {
            if (coordinates[i].Distance(coordinates[i - 1]) > thresholds.VertexTolerance)
            {
                continue;
            }

            duplicates++;
            first ??= coordinates[i];
        }

        // A closed ring legitimately repeats its first coordinate at the end.
        int expected = geometry is Polygon or LinearRing ? CountRings(geometry) : 0;

        if (duplicates <= expected)
        {
            return;
        }

        issues.Add(Issue(
            IssueSeverity.Warning,
            IssueCategory.Geometry,
            "Geometry.DuplicateVertices",
            $"{duplicates - expected} consecutive vertices repeat within {thresholds.VertexTolerance:G6}.",
            featureId,
            first));
    }

    private static int CountRings(NtsGeometry geometry) =>
        geometry is Polygon polygon ? 1 + polygon.NumInteriorRings : 1;

    /// <summary>Maps a topology error onto a severity by what it actually costs downstream.</summary>
    private static IssueSeverity Severity(TopologyValidationError? error) => error?.ErrorType switch
    {
        TopologyValidationErrors.SelfIntersection => IssueSeverity.Error,
        TopologyValidationErrors.RingSelfIntersection => IssueSeverity.Error,
        TopologyValidationErrors.HoleOutsideShell => IssueSeverity.Error,
        TopologyValidationErrors.NestedHoles => IssueSeverity.Error,
        TopologyValidationErrors.NestedShells => IssueSeverity.Error,
        TopologyValidationErrors.DisconnectedInteriors => IssueSeverity.Error,
        TopologyValidationErrors.DuplicateRings => IssueSeverity.Warning,
        TopologyValidationErrors.TooFewPoints => IssueSeverity.Error,
        TopologyValidationErrors.InvalidCoordinate => IssueSeverity.Critical,
        TopologyValidationErrors.RingNotClosed => IssueSeverity.Error,
        _ => IssueSeverity.Warning,
    };

    private static ValidationIssue Issue(
        IssueSeverity severity,
        IssueCategory category,
        string code,
        string message,
        string featureId,
        Coordinate? location = null)
    {
        ValidationIssue issue = ValidationIssue.Create(severity, category, code, message).ForFeature(featureId);

        return location is null ? issue : issue.At(location.X, location.Y);
    }
}
