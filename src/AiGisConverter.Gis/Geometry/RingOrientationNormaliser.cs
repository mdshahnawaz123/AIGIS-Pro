using AiGisConverter.Gis.Profiles;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Geometry;

/// <summary>
/// Forces polygon rings into the winding order a target format expects.
/// </summary>
/// <remarks>
/// The two mainstream formats disagree, and neither tolerates the other's convention silently.
/// RFC 7946 mandates counter-clockwise exterior rings; ESRI Shapefile mandates clockwise, and a
/// Shapefile written the other way renders as a hole punched through the world in some clients.
/// Normalising at the export boundary is the only place this can be got right once.
/// </remarks>
public static class RingOrientationNormaliser
{
    /// <summary>Applies an orientation rule to a geometry.</summary>
    /// <param name="geometry">The geometry to normalise.</param>
    /// <param name="rule">The convention to impose.</param>
    /// <returns>The normalised geometry, or the input when the rule is <see cref="RingOrientationRule.Preserve"/>.</returns>
    public static NtsGeometry Normalise(NtsGeometry geometry, RingOrientationRule rule)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (rule == RingOrientationRule.Preserve)
        {
            return geometry;
        }

        bool exteriorCounterClockwise = rule == RingOrientationRule.CounterClockwise;

        return geometry switch
        {
            Polygon polygon => NormalisePolygon(polygon, exteriorCounterClockwise),
            MultiPolygon multi => multi.Factory.CreateMultiPolygon(
                [.. multi.Geometries.Cast<Polygon>().Select(p => NormalisePolygon(p, exteriorCounterClockwise))]),
            GeometryCollection collection => collection.Factory.CreateGeometryCollection(
                [.. collection.Geometries.Select(g => Normalise(g, rule))]),
            _ => geometry,
        };
    }

    private static Polygon NormalisePolygon(Polygon polygon, bool exteriorCounterClockwise)
    {
        GeometryFactory factory = polygon.Factory;

        LinearRing shell = Orient(factory, polygon.ExteriorRing, exteriorCounterClockwise);
        LinearRing[] holes = new LinearRing[polygon.NumInteriorRings];

        for (int i = 0; i < polygon.NumInteriorRings; i++)
        {
            // Holes always wind opposite to the shell, whichever convention the shell follows.
            holes[i] = Orient(factory, polygon.GetInteriorRingN(i), !exteriorCounterClockwise);
        }

        return factory.CreatePolygon(shell, holes);
    }

    private static LinearRing Orient(GeometryFactory factory, LineString ring, bool counterClockwise)
    {
        Coordinate[] coordinates = ring.Coordinates;
        bool isCounterClockwise = Orientation.IsCCW(coordinates);

        if (isCounterClockwise == counterClockwise)
        {
            return factory.CreateLinearRing(coordinates);
        }

        Coordinate[] reversed = new Coordinate[coordinates.Length];

        for (int i = 0; i < coordinates.Length; i++)
        {
            reversed[i] = coordinates[coordinates.Length - 1 - i].Copy();
        }

        return factory.CreateLinearRing(reversed);
    }
}
