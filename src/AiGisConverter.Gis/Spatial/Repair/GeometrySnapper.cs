using AiGisConverter.Domain.Common;
using AiGisConverter.Gis.Spatial.Abstractions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Repair;

/// <summary>
/// Snaps near-coincident vertices together.
/// </summary>
/// <remarks>
/// <para>
/// The defect this addresses is the commonest in converted CAD data: two parcels digitised
/// separately share what the draughtsman intended as one boundary, but their vertices differ by a
/// few microns. Every overlay between them then produces sliver polygons a few square millimetres
/// in size, and a dissolve leaves hairline gaps that no one can see but every topology check
/// reports.
/// </para>
/// <para>
/// Snapping is destructive and is therefore not applied by default. The tolerance must be chosen
/// with the survey in mind: large enough to close digitising noise, small enough not to collapse a
/// genuinely narrow feature. A kerb line 50 mm wide disappears under a 100 mm tolerance.
/// </para>
/// </remarks>
public sealed class GeometrySnapper : IGeometrySnapper
{
    /// <inheritdoc />
    public Result<NtsGeometry> SnapToSelf(NtsGeometry geometry, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (tolerance <= 0d)
        {
            return Result.Success(geometry);
        }

        if (!double.IsFinite(tolerance))
        {
            return Result.Failure<NtsGeometry>(new Error(
                "Spatial.InvalidTolerance",
                "The snap tolerance must be a finite, positive number."));
        }

        try
        {
            // Reducing to a fixed grid collapses vertices that differ by less than the grid size;
            // the zero-width buffer then rebuilds the topology the collapse may have broken. This
            // is the long-standing formulation, using API stable across NetTopologySuite releases.
            PrecisionModel model = new(1d / tolerance);
            NtsGeometry reduced = new GeometryPrecisionReducer(model)
            {
                ChangePrecisionModel = true,
                Pointwise = false,
            }.Reduce(geometry);

            NtsGeometry snapped = reduced.IsValid ? reduced : reduced.Buffer(0d);

            return snapped.IsEmpty
                ? Result.Failure<NtsGeometry>(new Error(
                    "Spatial.SnapCollapsed",
                    $"Snapping at {tolerance:G6} collapsed the geometry entirely. The tolerance is too coarse for this feature."))
                : Result.Success(snapped);
        }
        catch (TopologyException ex)
        {
            return Result.Failure<NtsGeometry>(new Error("Spatial.SnapFailed", ex.Message));
        }
    }

    /// <inheritdoc />
    public Result<NtsGeometry> SnapTo(NtsGeometry geometry, NtsGeometry reference, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(reference);

        if (tolerance <= 0d)
        {
            return Result.Success(geometry);
        }

        try
        {
            NtsGeometry snapped = NetTopologySuite.Operation.Overlay.Snap.GeometrySnapper
                .SnapToSelf(geometry, tolerance, cleanResult: true);

            NtsGeometry[] pair = NetTopologySuite.Operation.Overlay.Snap.GeometrySnapper
                .Snap(snapped, reference, tolerance);

            return Result.Success(pair[0]);
        }
        catch (TopologyException ex)
        {
            return Result.Failure<NtsGeometry>(new Error("Spatial.SnapFailed", ex.Message));
        }
    }

    /// <inheritdoc />
    public NtsGeometry RemoveDuplicateVertices(NtsGeometry geometry, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (tolerance <= 0d || geometry.IsEmpty)
        {
            return geometry;
        }

        return geometry switch
        {
            LineString line => Rebuild(line, tolerance),
            Polygon polygon => Rebuild(polygon, tolerance),
            GeometryCollection collection => collection.Factory.BuildGeometry(
                [.. collection.Geometries.Select(g => RemoveDuplicateVertices(g, tolerance))]),
            _ => geometry,
        };
    }

    private static NtsGeometry Rebuild(LineString line, double tolerance)
    {
        Coordinate[] distinct = Deduplicate(line.Coordinates, tolerance);

        return distinct.Length >= 2 ? line.Factory.CreateLineString(distinct) : line;
    }

    private static NtsGeometry Rebuild(Polygon polygon, double tolerance)
    {
        LinearRing? shell = RebuildRing(polygon.ExteriorRing, tolerance, polygon.Factory);

        if (shell is null)
        {
            return polygon;
        }

        List<LinearRing> holes = [];

        foreach (LineString ring in polygon.InteriorRings)
        {
            LinearRing? rebuilt = RebuildRing(ring, tolerance, polygon.Factory);

            if (rebuilt is not null)
            {
                holes.Add(rebuilt);
            }
        }

        return polygon.Factory.CreatePolygon(shell, [.. holes]);
    }

    private static LinearRing? RebuildRing(LineString ring, double tolerance, GeometryFactory factory)
    {
        Coordinate[] distinct = Deduplicate(ring.Coordinates, tolerance);

        // A ring needs four coordinates with the first repeated at the end. Fewer means the
        // deduplication has destroyed it, and the original is the safer answer.
        if (distinct.Length < 4)
        {
            return null;
        }

        if (!distinct[0].Equals2D(distinct[^1]))
        {
            distinct = [.. distinct, distinct[0].Copy()];
        }

        return factory.CreateLinearRing(distinct);
    }

    private static Coordinate[] Deduplicate(Coordinate[] coordinates, double tolerance)
    {
        if (coordinates.Length == 0)
        {
            return coordinates;
        }

        List<Coordinate> distinct = new(coordinates.Length) { coordinates[0] };

        for (int i = 1; i < coordinates.Length; i++)
        {
            if (coordinates[i].Distance(distinct[^1]) > tolerance)
            {
                distinct.Add(coordinates[i]);
            }
        }

        return [.. distinct];
    }
}
