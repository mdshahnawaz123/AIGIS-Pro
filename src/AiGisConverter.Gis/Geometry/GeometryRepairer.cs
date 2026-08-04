using AiGisConverter.Gis.Abstractions;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Geometry;

/// <summary>
/// Default <see cref="IGeometryRepairer"/>.
/// </summary>
/// <remarks>
/// <para>
/// Repair is deliberately conservative and always reports what it did. A conversion tool that
/// silently changes a surveyed boundary to make it valid has produced a plausible file containing
/// a different fact, which is worse than producing nothing.
/// </para>
/// <para>
/// The zero-width buffer is the workhorse: it rebuilds a polygon's topology from its edges,
/// resolving self-intersections into separate rings. It is used rather than a fixer utility
/// because it has been in NetTopologySuite since the beginning and behaves identically to the JTS
/// original, which matters when output must match what a GIS analyst gets from PostGIS.
/// </para>
/// </remarks>
public sealed class GeometryRepairer : IGeometryRepairer
{
    /// <inheritdoc />
    public GeometryRepairResult Repair(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.IsValid)
        {
            return GeometryRepairResult.Unchanged(geometry);
        }

        return geometry switch
        {
            Polygon or MultiPolygon => RepairPolygonal(geometry),
            LineString or MultiLineString => RepairLinear(geometry),
            GeometryCollection collection => RepairCollection(collection),
            _ => GeometryRepairResult.Failed($"no repair strategy for {geometry.GeometryType}"),
        };
    }

    private static GeometryRepairResult RepairPolygonal(NtsGeometry geometry)
    {
        double originalArea = geometry.Area;

        try
        {
            NtsGeometry repaired = geometry.Buffer(0d);

            if (repaired.IsEmpty || !repaired.IsValid)
            {
                return GeometryRepairResult.Failed("zero-width buffer produced no valid geometry");
            }

            double ratio = originalArea > 0d
                ? Math.Abs(repaired.Area - originalArea) / originalArea
                : 0d;

            return new GeometryRepairResult(repaired, true, "zero-width buffer", ratio);
        }
        catch (Exception ex) when (ex is TopologyException or ArgumentException)
        {
            return GeometryRepairResult.Failed($"zero-width buffer failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Repairs a line by removing repeated vertices.
    /// </summary>
    /// <remarks>
    /// A self-crossing line is legal, so there is nothing to repair; the only structural fault a
    /// line can have is too few distinct points, and that is recoverable only by de-duplication.
    /// </remarks>
    private static GeometryRepairResult RepairLinear(NtsGeometry geometry)
    {
        GeometryFactory factory = geometry.Factory;

        if (geometry is LineString line)
        {
            Coordinate[] distinct = RemoveRepeated(line.Coordinates);

            return distinct.Length >= 2
                ? new GeometryRepairResult(factory.CreateLineString(distinct), true, "removed repeated vertices", 0d)
                : GeometryRepairResult.Failed("fewer than two distinct vertices remain");
        }

        List<LineString> parts = [];

        for (int i = 0; i < geometry.NumGeometries; i++)
        {
            Coordinate[] distinct = RemoveRepeated(geometry.GetGeometryN(i).Coordinates);

            if (distinct.Length >= 2)
            {
                parts.Add(factory.CreateLineString(distinct));
            }
        }

        return parts.Count > 0
            ? new GeometryRepairResult(factory.CreateMultiLineString([.. parts]), true, "removed repeated vertices", 0d)
            : GeometryRepairResult.Failed("no part retained two distinct vertices");
    }

    /// <summary>Repairs each member, keeping those that survive.</summary>
    private GeometryRepairResult RepairCollection(GeometryCollection collection)
    {
        List<NtsGeometry> repaired = [];
        int failures = 0;

        foreach (NtsGeometry part in collection.Geometries)
        {
            GeometryRepairResult result = Repair(part);

            if (result.Succeeded && result.Geometry is not null)
            {
                repaired.Add(result.Geometry);
            }
            else
            {
                failures++;
            }
        }

        if (repaired.Count == 0)
        {
            return GeometryRepairResult.Failed("no member of the collection could be repaired");
        }

        return new GeometryRepairResult(
            collection.Factory.CreateGeometryCollection([.. repaired]),
            true,
            failures == 0 ? "repaired every member" : $"repaired {repaired.Count} members, dropped {failures}",
            0d);
    }

    private static Coordinate[] RemoveRepeated(Coordinate[] coordinates)
    {
        if (coordinates.Length == 0)
        {
            return coordinates;
        }

        List<Coordinate> distinct = new(coordinates.Length) { coordinates[0] };

        for (int i = 1; i < coordinates.Length; i++)
        {
            if (!coordinates[i].Equals2D(distinct[^1]))
            {
                distinct.Add(coordinates[i]);
            }
        }

        return [.. distinct];
    }
}
