using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.ValueObjects;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Abstractions;

/// <summary>
/// Measurements over geometry, aware of the coordinate system the geometry is in.
/// </summary>
/// <remarks>
/// <para>
/// Every measuring method takes the coordinate system. That is the whole point of this interface.
/// NetTopologySuite computes planar area and length; applied to WGS 84 that yields square degrees
/// and degrees, which are not areas and not lengths. The number looks plausible, propagates into a
/// report, and nobody notices.
/// </para>
/// <para>
/// So a measurement in a geographic system is computed geodesically and returned in metres, and a
/// measurement in a projected system is computed planar and returned in that system's linear
/// units. The result states which happened.
/// </para>
/// </remarks>
public interface ISpatialAnalysis
{
    /// <summary>Computes area.</summary>
    /// <param name="geometry">The geometry to measure.</param>
    /// <param name="coordinateSystem">The system the coordinates are in.</param>
    /// <returns>The area, or a failure when the system makes it meaningless.</returns>
    Result<Measurement> Area(NtsGeometry geometry, CoordinateSystem coordinateSystem);

    /// <summary>Computes length or perimeter.</summary>
    /// <param name="geometry">The geometry to measure.</param>
    /// <param name="coordinateSystem">The system the coordinates are in.</param>
    /// <returns>The length, or a failure when the system makes it meaningless.</returns>
    Result<Measurement> Length(NtsGeometry geometry, CoordinateSystem coordinateSystem);

    /// <summary>Computes the distance between two geometries.</summary>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <param name="coordinateSystem">The system the coordinates are in.</param>
    /// <returns>The distance, or a failure when the system makes it meaningless.</returns>
    Result<Measurement> Distance(NtsGeometry left, NtsGeometry right, CoordinateSystem coordinateSystem);

    /// <summary>
    /// Computes the centroid.
    /// </summary>
    /// <remarks>
    /// The centroid of a crescent or a horseshoe lies outside it. Use
    /// <see cref="PointOnSurface"/> when the result must be inside the geometry, which is what
    /// label placement needs.
    /// </remarks>
    /// <param name="geometry">The geometry to measure.</param>
    /// <returns>The centroid, or null for empty geometry.</returns>
    Point? Centroid(NtsGeometry geometry);

    /// <summary>Computes a point guaranteed to lie on or inside the geometry.</summary>
    /// <param name="geometry">The geometry to measure.</param>
    /// <returns>The point, or null for empty geometry.</returns>
    Point? PointOnSurface(NtsGeometry geometry);

    /// <summary>Computes the axis-aligned bounding box.</summary>
    /// <param name="geometry">The geometry to measure.</param>
    /// <returns>The extent, or the empty extent for empty geometry.</returns>
    Extent BoundingBox(NtsGeometry geometry);

    /// <summary>Computes the smallest convex geometry enclosing the input.</summary>
    /// <param name="geometry">The geometry to enclose.</param>
    /// <returns>The convex hull.</returns>
    NtsGeometry ConvexHull(NtsGeometry geometry);

    /// <summary>Finds the nearest of a set of geometries.</summary>
    /// <param name="target">The geometry to measure from.</param>
    /// <param name="candidates">The candidates.</param>
    /// <param name="coordinateSystem">The system the coordinates are in.</param>
    /// <param name="count">How many to return.</param>
    /// <returns>The nearest candidates with their distances, closest first.</returns>
    IReadOnlyList<NearestResult> Nearest(
        NtsGeometry target,
        IEnumerable<NtsGeometry> candidates,
        CoordinateSystem coordinateSystem,
        int count = 1);
}

/// <summary>A measurement and the units it is expressed in.</summary>
/// <param name="Value">The magnitude.</param>
/// <param name="Units">What the magnitude is in, for example <c>metre</c> or <c>square metre</c>.</param>
/// <param name="IsGeodetic">Whether the value was computed on the ellipsoid rather than in the plane.</param>
/// <param name="AccuracyNote">
/// How much to trust it. Populated for geodetic results, where a spherical approximation is used.
/// </param>
public sealed record Measurement(double Value, string Units, bool IsGeodetic, string? AccuracyNote = null)
{
    /// <inheritdoc />
    public override string ToString() => $"{Value:G6} {Units}";
}

/// <summary>One result of a nearest-neighbour search.</summary>
/// <param name="Geometry">The candidate.</param>
/// <param name="Distance">How far away it is.</param>
public sealed record NearestResult(NtsGeometry Geometry, Measurement Distance);
