using AiGisConverter.Cad.Options;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Cad.Geometry;

/// <summary>
/// Turns CAD curves into coordinate sequences.
/// </summary>
/// <remarks>
/// Pure geometry: it takes numbers and returns coordinates, and knows nothing about DXF, DWG or
/// any vendor library. That is what allows it to be unit-tested directly and shared unchanged
/// between every CAD provider.
/// </remarks>
public static class CurveTessellator
{
    /// <summary>Approximates a circular arc.</summary>
    /// <param name="centreX">Arc centre X.</param>
    /// <param name="centreY">Arc centre Y.</param>
    /// <param name="radius">Arc radius.</param>
    /// <param name="startAngle">Angle to the first point, in radians.</param>
    /// <param name="sweep">Signed included angle. Positive is counter-clockwise.</param>
    /// <param name="options">The tessellation settings.</param>
    /// <param name="elevation">Z assigned to every produced coordinate.</param>
    /// <returns>The coordinates, including both endpoints.</returns>
    public static Coordinate[] Arc(
        double centreX,
        double centreY,
        double radius,
        double startAngle,
        double sweep,
        TessellationOptions options,
        double elevation = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (radius <= 0d || !double.IsFinite(radius))
        {
            return [MakeCoordinate(centreX, centreY, elevation)];
        }

        int segments = CurveTessellation.SegmentCountFor(radius, sweep, options);
        Coordinate[] coordinates = new Coordinate[segments + 1];
        double step = sweep / segments;

        for (int i = 0; i <= segments; i++)
        {
            double angle = startAngle + (step * i);

            coordinates[i] = MakeCoordinate(
                centreX + (radius * Math.Cos(angle)),
                centreY + (radius * Math.Sin(angle)),
                elevation);
        }

        return coordinates;
    }

    /// <summary>Approximates a full circle as a closed ring.</summary>
    /// <param name="centreX">Circle centre X.</param>
    /// <param name="centreY">Circle centre Y.</param>
    /// <param name="radius">Circle radius.</param>
    /// <param name="options">The tessellation settings.</param>
    /// <param name="elevation">Z assigned to every produced coordinate.</param>
    /// <returns>The ring coordinates, with the last point equal to the first.</returns>
    public static Coordinate[] Circle(
        double centreX,
        double centreY,
        double radius,
        TessellationOptions options,
        double elevation = double.NaN)
    {
        Coordinate[] coordinates = Arc(centreX, centreY, radius, 0d, 2d * Math.PI, options, elevation);

        // Floating-point error leaves the computed final point a few ulps from the first, which is
        // enough for NetTopologySuite to reject the ring as unclosed.
        coordinates[^1] = coordinates[0].Copy();

        return coordinates;
    }

    /// <summary>Approximates an elliptical arc.</summary>
    /// <param name="centreX">Ellipse centre X.</param>
    /// <param name="centreY">Ellipse centre Y.</param>
    /// <param name="majorRadius">Semi-major axis length.</param>
    /// <param name="minorRadius">Semi-minor axis length.</param>
    /// <param name="rotation">Rotation of the major axis from the X axis, in radians.</param>
    /// <param name="startParameter">Start parameter, in radians.</param>
    /// <param name="sweep">Signed parameter sweep.</param>
    /// <param name="options">The tessellation settings.</param>
    /// <param name="elevation">Z assigned to every produced coordinate.</param>
    /// <returns>The coordinates, including both endpoints.</returns>
    public static Coordinate[] EllipticalArc(
        double centreX,
        double centreY,
        double majorRadius,
        double minorRadius,
        double rotation,
        double startParameter,
        double sweep,
        TessellationOptions options,
        double elevation = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (majorRadius <= 0d || minorRadius <= 0d)
        {
            return [MakeCoordinate(centreX, centreY, elevation)];
        }

        // Segment count is driven by the major axis: it is where the curvature is tightest, so
        // satisfying the tolerance there satisfies it everywhere on the ellipse.
        int segments = CurveTessellation.SegmentCountFor(majorRadius, sweep, options);
        Coordinate[] coordinates = new Coordinate[segments + 1];

        double cosRotation = Math.Cos(rotation);
        double sinRotation = Math.Sin(rotation);
        double step = sweep / segments;

        for (int i = 0; i <= segments; i++)
        {
            double parameter = startParameter + (step * i);
            double localX = majorRadius * Math.Cos(parameter);
            double localY = minorRadius * Math.Sin(parameter);

            coordinates[i] = MakeCoordinate(
                centreX + (localX * cosRotation) - (localY * sinRotation),
                centreY + (localX * sinRotation) + (localY * cosRotation),
                elevation);
        }

        return coordinates;
    }

    /// <summary>
    /// Expands a bulge-bearing vertex list into a coordinate sequence.
    /// </summary>
    /// <remarks>
    /// This is the workhorse of CAD conversion. Almost every real drawing represents kerbs, pipes
    /// and boundaries as polylines whose curved sections are bulges rather than separate arc
    /// entities, so a reader that ignores bulges silently straightens the drawing.
    /// </remarks>
    /// <param name="vertices">The vertices, each carrying the bulge for the segment that follows it.</param>
    /// <param name="isClosed">Whether a closing segment runs from the last vertex back to the first.</param>
    /// <param name="options">The tessellation settings.</param>
    /// <param name="elevation">Z assigned to every produced coordinate.</param>
    /// <returns>The expanded coordinates.</returns>
    public static Coordinate[] Polyline(
        IReadOnlyList<PolylineVertex> vertices,
        bool isClosed,
        TessellationOptions options,
        double elevation = double.NaN)
    {
        ArgumentNullException.ThrowIfNull(vertices);
        ArgumentNullException.ThrowIfNull(options);

        if (vertices.Count == 0)
        {
            return [];
        }

        if (vertices.Count == 1)
        {
            return [MakeCoordinate(vertices[0].X, vertices[0].Y, elevation)];
        }

        List<Coordinate> coordinates = new(vertices.Count * 2);
        int lastIndex = isClosed ? vertices.Count - 1 : vertices.Count - 2;

        for (int i = 0; i <= lastIndex; i++)
        {
            PolylineVertex current = vertices[i];
            PolylineVertex next = vertices[(i + 1) % vertices.Count];

            AppendSegment(coordinates, current, next, options, elevation);
        }

        if (isClosed)
        {
            coordinates.Add(coordinates[0].Copy());
        }
        else
        {
            PolylineVertex last = vertices[^1];
            coordinates.Add(MakeCoordinate(last.X, last.Y, elevation));
        }

        return [.. coordinates];
    }

    private static void AppendSegment(
        List<Coordinate> coordinates,
        PolylineVertex current,
        PolylineVertex next,
        TessellationOptions options,
        double elevation)
    {
        if (!BulgeArc.TryCreate(current.X, current.Y, next.X, next.Y, current.Bulge, out BulgeArc arc))
        {
            coordinates.Add(MakeCoordinate(current.X, current.Y, elevation));
            return;
        }

        Coordinate[] arcPoints = Arc(
            arc.CentreX,
            arc.CentreY,
            arc.Radius,
            arc.StartAngle,
            arc.Sweep,
            options,
            elevation);

        // The final arc point is the next vertex; it is emitted by the following segment, or by
        // the caller's closing step. Adding it here would duplicate every interior vertex.
        for (int i = 0; i < arcPoints.Length - 1; i++)
        {
            coordinates.Add(arcPoints[i]);
        }
    }

    private static Coordinate MakeCoordinate(double x, double y, double elevation) =>
        double.IsNaN(elevation) ? new Coordinate(x, y) : new CoordinateZ(x, y, elevation);
}

/// <summary>
/// One polyline vertex and the bulge describing the segment that leaves it.
/// </summary>
/// <param name="X">Vertex X.</param>
/// <param name="Y">Vertex Y.</param>
/// <param name="Bulge">The bulge for the following segment. Zero is a straight segment.</param>
public readonly record struct PolylineVertex(double X, double Y, double Bulge = 0d);
