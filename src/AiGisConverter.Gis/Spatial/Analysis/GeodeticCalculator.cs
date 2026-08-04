using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Analysis;

/// <summary>
/// Measures on the sphere, for geometry expressed in geographic coordinates.
/// </summary>
/// <remarks>
/// <para>
/// A spherical model is used rather than the full ellipsoid. The error against WGS 84 is about
/// 0.5% on area and 0.3% on distance &#8212; well inside the uncertainty of the CAD survey these
/// numbers come from, and vastly better than the alternative of reporting square degrees.
/// </para>
/// <para>
/// A caller needing survey-grade figures should reproject to an appropriate projected system and
/// measure in the plane; that path is available and is what <see cref="SpatialAnalysis"/> uses
/// whenever the data is already projected.
/// </para>
/// <para>
/// Verified numerically before implementation: the northern hemisphere computes to exactly half
/// the sphere, London to Paris to 343.6 km against a published 344 km, and a one-degree cell at
/// the equator to 12,364 km² against an ellipsoidal 12,308 km².
/// </para>
/// </remarks>
public static class GeodeticCalculator
{
    /// <summary>
    /// WGS 84 mean radius in metres, as defined by the IUGG.
    /// </summary>
    /// <remarks>
    /// The arithmetic mean of the three semi-axes. Chosen over the equatorial radius because it
    /// minimises error across all latitudes rather than being exact at one.
    /// </remarks>
    public const double EarthRadiusMetres = 6_371_008.8d;

    /// <summary>The accuracy caveat attached to every geodetic result.</summary>
    public const string AccuracyNote =
        "Computed on a sphere of mean radius 6,371,008.8 m. Expect within about 0.5% of the " +
        "ellipsoidal value. Reproject and measure in the plane if survey-grade figures are needed.";

    /// <summary>Computes the great-circle distance between two points.</summary>
    /// <remarks>
    /// The haversine formulation is used rather than the spherical law of cosines because the
    /// latter loses precision catastrophically at small distances, which is most of what a site
    /// survey contains.
    /// </remarks>
    /// <param name="longitude1">First longitude in degrees.</param>
    /// <param name="latitude1">First latitude in degrees.</param>
    /// <param name="longitude2">Second longitude in degrees.</param>
    /// <param name="latitude2">Second latitude in degrees.</param>
    /// <returns>The distance in metres.</returns>
    public static double Distance(double longitude1, double latitude1, double longitude2, double latitude2)
    {
        double phi1 = ToRadians(latitude1);
        double phi2 = ToRadians(latitude2);
        double deltaPhi = phi2 - phi1;
        double deltaLambda = ToRadians(longitude2 - longitude1);

        double a = (Math.Sin(deltaPhi / 2d) * Math.Sin(deltaPhi / 2d))
                   + (Math.Cos(phi1) * Math.Cos(phi2) * Math.Sin(deltaLambda / 2d) * Math.Sin(deltaLambda / 2d));

        return 2d * EarthRadiusMetres * Math.Asin(Math.Min(1d, Math.Sqrt(a)));
    }

    /// <summary>Computes the total geodesic length of every line in a geometry.</summary>
    /// <param name="geometry">The geometry to measure.</param>
    /// <returns>The length in metres.</returns>
    public static double Length(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return geometry switch
        {
            Point => 0d,
            LineString line => LengthOf(line.Coordinates),
            Polygon polygon => LengthOf(polygon.ExteriorRing.Coordinates)
                               + polygon.InteriorRings.Sum(ring => LengthOf(ring.Coordinates)),
            GeometryCollection collection => collection.Geometries.Sum(Length),
            _ => 0d,
        };
    }

    /// <summary>
    /// Computes the geodesic area of a geometry using the spherical excess.
    /// </summary>
    /// <remarks>
    /// The Chamberlain and Duquette formulation, which sums a signed contribution per edge and so
    /// handles holes correctly by their winding without needing to identify them.
    /// </remarks>
    /// <param name="geometry">The geometry to measure.</param>
    /// <returns>The area in square metres.</returns>
    public static double Area(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return geometry switch
        {
            Polygon polygon => Math.Abs(SignedRingArea(polygon.ExteriorRing.Coordinates))
                               - polygon.InteriorRings.Sum(ring => Math.Abs(SignedRingArea(ring.Coordinates))),
            GeometryCollection collection => collection.Geometries.Sum(Area),
            _ => 0d,
        };
    }

    /// <summary>Computes the signed spherical area enclosed by a ring, in square metres.</summary>
    private static double SignedRingArea(Coordinate[] ring)
    {
        if (ring.Length < 4)
        {
            return 0d;
        }

        double total = 0d;

        for (int i = 0; i < ring.Length - 1; i++)
        {
            double lambda1 = ToRadians(ring[i].X);
            double phi1 = ToRadians(ring[i].Y);
            double lambda2 = ToRadians(ring[i + 1].X);
            double phi2 = ToRadians(ring[i + 1].Y);

            total += (lambda2 - lambda1) * (2d + Math.Sin(phi1) + Math.Sin(phi2));
        }

        return total * EarthRadiusMetres * EarthRadiusMetres / 2d;
    }

    private static double LengthOf(Coordinate[] coordinates)
    {
        double total = 0d;

        for (int i = 1; i < coordinates.Length; i++)
        {
            total += Distance(coordinates[i - 1].X, coordinates[i - 1].Y, coordinates[i].X, coordinates[i].Y);
        }

        return total;
    }

    private static double ToRadians(double degrees) => degrees * Math.PI / 180d;
}
