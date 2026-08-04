using NetTopologySuite.Geometries;

namespace AiGisConverter.Cad.Geometry;

/// <summary>
/// Builds valid polygons from the loose set of closed rings a hatch boundary provides.
/// </summary>
/// <remarks>
/// <para>
/// A DXF hatch is a bag of boundary loops with no reliable statement of which is an outer boundary
/// and which is a hole. The island flags exist but are widely wrong in files produced by
/// third-party exporters, so containment is determined geometrically instead: a ring nested inside
/// an odd number of others is a hole.
/// </para>
/// <para>
/// Rings are also frequently unclosed by a few microns, and occasionally self-touching. Both are
/// repaired here rather than being allowed to reach the exporter, where they would surface as an
/// opaque topology error from GDAL.
/// </para>
/// </remarks>
public static class PolygonAssembler
{
    private static readonly GeometryFactory Factory = new();

    /// <summary>
    /// Assembles rings into a polygon or multipolygon.
    /// </summary>
    /// <param name="rings">The boundary loops, each a closed or nearly closed coordinate sequence.</param>
    /// <returns>
    /// The assembled geometry, or <see langword="null"/> when no ring had enough distinct points
    /// to form an area.
    /// </returns>
    public static NetTopologySuite.Geometries.Geometry? Assemble(IReadOnlyList<Coordinate[]> rings)
    {
        ArgumentNullException.ThrowIfNull(rings);

        List<LinearRing> valid = [];

        foreach (Coordinate[] ring in rings)
        {
            if (TryCloseRing(ring, out LinearRing? closed))
            {
                valid.Add(closed!);
            }
        }

        if (valid.Count == 0)
        {
            return null;
        }

        if (valid.Count == 1)
        {
            return Factory.CreatePolygon(valid[0]);
        }

        return AssembleWithHoles(valid);
    }

    /// <summary>
    /// Closes a ring and rejects degenerate ones.
    /// </summary>
    /// <remarks>
    /// A linear ring needs four coordinates with the first repeated at the end. Drawings routinely
    /// supply three, or supply a fourth that differs from the first by a rounding error, and
    /// NetTopologySuite rejects both.
    /// </remarks>
    /// <param name="coordinates">The candidate ring.</param>
    /// <param name="ring">The closed ring, when one could be formed.</param>
    /// <returns><see langword="true"/> when a valid ring was produced.</returns>
    public static bool TryCloseRing(Coordinate[] coordinates, out LinearRing? ring)
    {
        ring = null;

        if (coordinates is null || coordinates.Length < 3)
        {
            return false;
        }

        List<Coordinate> distinct = new(coordinates.Length + 1) { coordinates[0] };

        for (int i = 1; i < coordinates.Length; i++)
        {
            if (!coordinates[i].Equals2D(distinct[^1]))
            {
                distinct.Add(coordinates[i]);
            }
        }

        // Drop a trailing duplicate of the start so the closing step below is unambiguous.
        while (distinct.Count > 1 && distinct[^1].Equals2D(distinct[0]))
        {
            distinct.RemoveAt(distinct.Count - 1);
        }

        if (distinct.Count < 3 || SignedArea(distinct) == 0d)
        {
            // NetTopologySuite validates point count and closure but not area, so a run of
            // collinear vertices would otherwise become a zero-area polygon that survives all the
            // way to the exporter before anything objects.
            return false;
        }

        distinct.Add(distinct[0].Copy());

        try
        {
            ring = Factory.CreateLinearRing([.. distinct]);
            return true;
        }
        catch (ArgumentException)
        {
            // NetTopologySuite rejects rings it cannot use. One bad hatch must not fail the file.
            return false;
        }
    }

    /// <summary>Shoelace area of an open coordinate ring. Sign indicates orientation.</summary>
    private static double SignedArea(IReadOnlyList<Coordinate> ring)
    {
        double twiceArea = 0d;

        for (int i = 0; i < ring.Count; i++)
        {
            Coordinate current = ring[i];
            Coordinate next = ring[(i + 1) % ring.Count];

            twiceArea += (current.X * next.Y) - (next.X * current.Y);
        }

        return twiceArea / 2d;
    }


    /// <summary>
    /// Determines whether one ring lies wholly inside another.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The test is containment of <paramref name="inner"/>'s <em>boundary</em>, not of a single
    /// representative point. Concentric rings share an interior point — the centre of a square
    /// hatch is also the centre of the square hole inside it — so a point test reports every ring
    /// in a concentric stack as being inside every other, and the resulting depths are all equal.
    /// Equal depths mean no ring is ever recognised as a hole.
    /// </para>
    /// <para>
    /// That failure is silent and it is the common case, not the exotic one: a courtyard inside a
    /// plot, an annulus, a road with a median. The polygon still exports, still validates, and
    /// still reports an area — just the wrong one, inflated by the size of every hole it failed to
    /// subtract.
    /// </para>
    /// </remarks>
    /// <param name="outer">The candidate enclosing ring.</param>
    /// <param name="inner">The candidate enclosed ring.</param>
    /// <returns><see langword="true"/> when <paramref name="inner"/> lies inside <paramref name="outer"/>.</returns>
    private static bool Encloses(Polygon outer, Polygon inner) =>

        // The envelope test is a cheap reject that keeps the quadratic scan affordable on hatches
        // with many loops; only survivors pay for the linework comparison.
        outer.EnvelopeInternal.Contains(inner.EnvelopeInternal)
        && outer.Covers(inner.ExteriorRing);

    /// <summary>Nests rings by containment and emits one polygon per outermost ring.</summary>
    private static NetTopologySuite.Geometries.Geometry AssembleWithHoles(List<LinearRing> rings)
    {
        Polygon[] candidates = [.. rings.Select(ring => Factory.CreatePolygon(ring))];
        int[] depth = new int[candidates.Length];

        for (int i = 0; i < candidates.Length; i++)
        {
            for (int j = 0; j < candidates.Length; j++)
            {
                if (i != j && Encloses(candidates[j], candidates[i]))
                {
                    depth[i]++;
                }
            }
        }

        List<Polygon> assembled = [];

        for (int i = 0; i < candidates.Length; i++)
        {
            // Even depth means an outer boundary; odd means a hole in the ring that encloses it.
            if (depth[i] % 2 != 0)
            {
                continue;
            }

            List<LinearRing> holes = [];

            for (int j = 0; j < candidates.Length; j++)
            {
                if (i != j && depth[j] == depth[i] + 1 && Encloses(candidates[i], candidates[j]))
                {
                    holes.Add((LinearRing)candidates[j].ExteriorRing);
                }
            }

            assembled.Add(Factory.CreatePolygon((LinearRing)candidates[i].ExteriorRing, [.. holes]));
        }

        return assembled.Count switch
        {
            0 => Factory.CreatePolygon(rings[0]),
            1 => assembled[0],
            _ => Factory.CreateMultiPolygon([.. assembled]),
        };
    }
}
