using System.Globalization;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Plugins.LandXml;

/// <summary>
/// Converts LandXML's coordinate text and curve definitions into planar geometry.
/// </summary>
/// <remarks>
/// <para>
/// LandXML writes coordinates as <c>northing easting [elevation]</c> — northing first. Every GIS
/// consumer expects x (easting) first, so the order is swapped here, once, at the boundary. Getting
/// this wrong does not fail: it silently mirrors the site about the 45 degree line, which is why it
/// is done in one place with tests rather than at each call site.
/// </para>
/// <para>
/// Arcs are tessellated rather than approximated by their chord. A parcel boundary closed with a
/// chord loses real area, and area is the number a parcel is bought and sold on.
/// </para>
/// </remarks>
internal static class LandXmlGeometry
{
    /// <summary>The default number of segments used to tessellate a full circle.</summary>
    internal const int SegmentsPerCircle = 72;

    /// <summary>
    /// Parses a LandXML coordinate triple, swapping to easting-first order.
    /// </summary>
    /// <param name="text">Whitespace-separated <c>northing easting [elevation]</c>.</param>
    /// <param name="coordinate">The parsed coordinate, with x = easting and y = northing.</param>
    /// <returns><see langword="true"/> when at least two ordinates were read.</returns>
    internal static bool TryParseCoordinate(string? text, out Coordinate coordinate)
    {
        coordinate = new Coordinate();

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        string[] parts = text.Split(
            [' ', '\t', '\r', '\n', ','],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (parts.Length < 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double northing)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double easting))
        {
            return false;
        }

        coordinate = parts.Length >= 3
            && double.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double elevation)
                ? new CoordinateZ(easting, northing, elevation)
                : new Coordinate(easting, northing);

        return true;
    }

    /// <summary>
    /// Parses a whitespace-separated list of coordinate triples, as used by <c>PntList3D</c>.
    /// </summary>
    /// <param name="text">The raw element text.</param>
    /// <returns>The coordinates in easting-first order. Empty when none could be read.</returns>
    internal static IReadOnlyList<Coordinate> ParseCoordinateList(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string[] parts = text.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<Coordinate> coordinates = [];

        // PntList3D is a flat run of northing/easting/elevation triples.
        for (int i = 0; i + 2 < parts.Length; i += 3)
        {
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double northing)
                && double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double easting)
                && double.TryParse(parts[i + 2], NumberStyles.Float, CultureInfo.InvariantCulture, out double elevation))
            {
                coordinates.Add(new CoordinateZ(easting, northing, elevation));
            }
        }

        return coordinates;
    }

    /// <summary>
    /// Tessellates a LandXML circular arc between two points about a centre.
    /// </summary>
    /// <remarks>
    /// LandXML gives start, centre and end plus a rotation direction. The sweep is derived from the
    /// bearings to the endpoints rather than from the stated length, because the two disagree in
    /// files from several exporters and the geometry is what must close.
    /// </remarks>
    /// <param name="start">The arc's start point.</param>
    /// <param name="centre">The arc's centre.</param>
    /// <param name="end">The arc's end point.</param>
    /// <param name="clockwise">Whether the arc turns clockwise from start to end.</param>
    /// <returns>The tessellated arc including both endpoints.</returns>
    internal static IReadOnlyList<Coordinate> TessellateArc(
        Coordinate start,
        Coordinate centre,
        Coordinate end,
        bool clockwise)
    {
        double radius = Math.Sqrt(
            ((start.X - centre.X) * (start.X - centre.X)) + ((start.Y - centre.Y) * (start.Y - centre.Y)));

        if (radius <= 0d || !double.IsFinite(radius))
        {
            return [start, end];
        }

        double startAngle = Math.Atan2(start.Y - centre.Y, start.X - centre.X);
        double endAngle = Math.Atan2(end.Y - centre.Y, end.X - centre.X);

        double sweep = endAngle - startAngle;

        // Normalise the sweep into the stated direction. A clockwise arc has a negative sweep.
        if (clockwise)
        {
            while (sweep > 0d)
            {
                sweep -= 2d * Math.PI;
            }
        }
        else
        {
            while (sweep < 0d)
            {
                sweep += 2d * Math.PI;
            }
        }

        int segments = Math.Max(2, (int)Math.Ceiling(Math.Abs(sweep) / (2d * Math.PI) * SegmentsPerCircle));
        List<Coordinate> coordinates = new(segments + 1);

        for (int i = 0; i <= segments; i++)
        {
            double angle = startAngle + (sweep * i / segments);
            coordinates.Add(new Coordinate(
                centre.X + (radius * Math.Cos(angle)),
                centre.Y + (radius * Math.Sin(angle))));
        }

        // Pin the endpoints to the stated values so consecutive segments join exactly.
        coordinates[0] = start;
        coordinates[^1] = end;

        return coordinates;
    }

    /// <summary>
    /// Builds one TIN face as a closed triangular ring.
    /// </summary>
    /// <remarks>
    /// A LandXML face references three points by id. The ring is closed by repeating the first
    /// vertex, and wound counter-clockwise so the polygon has positive area — the orientation the
    /// OGC simple-feature model expects for an exterior ring, and the one every downstream area
    /// calculation assumes.
    /// </remarks>
    /// <param name="a">The first vertex.</param>
    /// <param name="b">The second vertex.</param>
    /// <param name="c">The third vertex.</param>
    /// <returns>Four coordinates: the triangle, closed, counter-clockwise.</returns>
    internal static IReadOnlyList<Coordinate> BuildTriangleRing(Coordinate a, Coordinate b, Coordinate c)
    {
        // Twice the signed area. Negative means the vertices are wound clockwise.
        double twiceArea =
            ((b.X - a.X) * (c.Y - a.Y))
            - ((c.X - a.X) * (b.Y - a.Y));

        return twiceArea < 0d
            ? [a, c, b, a.Copy()]
            : [a, b, c, a.Copy()];
    }

    /// <summary>Determines whether a closed ring is wound counter-clockwise.</summary>
    /// <param name="ring">The ring to test. The first and last coordinates should match.</param>
    /// <returns><see langword="true"/> when the ring encloses a positive area.</returns>
    internal static bool IsCounterClockwise(IReadOnlyList<Coordinate> ring)
    {
        double twiceArea = 0d;

        for (int i = 0; i < ring.Count - 1; i++)
        {
            twiceArea += (ring[i].X * ring[i + 1].Y) - (ring[i + 1].X * ring[i].Y);
        }

        return twiceArea > 0d;
    }

    /// <summary>Closes a ring by repeating its first coordinate, when it is not already closed.</summary>
    /// <param name="coordinates">The boundary coordinates.</param>
    /// <returns>A closed ring, or the input when it is too short to close.</returns>
    internal static IReadOnlyList<Coordinate> CloseRing(IReadOnlyList<Coordinate> coordinates)
    {
        if (coordinates.Count < 3)
        {
            return coordinates;
        }

        if (coordinates[0].Equals2D(coordinates[^1]))
        {
            return coordinates;
        }

        List<Coordinate> closed = [.. coordinates, coordinates[0].Copy()];

        return closed;
    }

    /// <summary>Appends coordinates to a run, skipping a duplicate of the previous point.</summary>
    /// <param name="target">The run being built.</param>
    /// <param name="addition">The coordinates to append.</param>
    internal static void AppendWithoutDuplicate(List<Coordinate> target, IReadOnlyList<Coordinate> addition)
    {
        foreach (Coordinate coordinate in addition)
        {
            if (target.Count == 0 || !target[^1].Equals2D(coordinate))
            {
                target.Add(coordinate);
            }
        }
    }
}
