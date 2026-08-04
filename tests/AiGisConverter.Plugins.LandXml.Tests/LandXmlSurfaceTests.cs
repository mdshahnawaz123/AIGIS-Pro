using AiGisConverter.Plugins.LandXml;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Plugins.LandXml.Tests;

/// <summary>
/// TIN surface tests: triangle construction, ring orientation and boundary closure.
/// </summary>
/// <remarks>
/// Orientation is asserted rather than assumed. A clockwise exterior ring has negative area under
/// the OGC model, and every downstream area figure — parcel sizes, cut and fill volumes — inherits
/// that sign.
/// </remarks>
public sealed class LandXmlSurfaceTests
{
    private static readonly GeometryFactory Factory = new();

    [Fact]
    public void Triangle_IsClosed_WithFourCoordinates()
    {
        IReadOnlyList<Coordinate> ring = LandXmlGeometry.BuildTriangleRing(
            new Coordinate(0d, 0d), new Coordinate(10d, 0d), new Coordinate(0d, 10d));

        ring.Should().HaveCount(4, "a closed triangle repeats its first vertex");
        ring[0].Equals2D(ring[^1]).Should().BeTrue();
    }

    [Fact]
    public void Triangle_WoundClockwise_IsReversedToCounterClockwise()
    {
        // Listed clockwise: (0,0) -> (0,10) -> (10,0).
        IReadOnlyList<Coordinate> ring = LandXmlGeometry.BuildTriangleRing(
            new Coordinate(0d, 0d), new Coordinate(0d, 10d), new Coordinate(10d, 0d));

        LandXmlGeometry.IsCounterClockwise(ring).Should().BeTrue(
            "an exterior ring must enclose a positive area");
    }

    [Fact]
    public void Triangle_AlreadyCounterClockwise_IsLeftAlone()
    {
        IReadOnlyList<Coordinate> ring = LandXmlGeometry.BuildTriangleRing(
            new Coordinate(0d, 0d), new Coordinate(10d, 0d), new Coordinate(0d, 10d));

        LandXmlGeometry.IsCounterClockwise(ring).Should().BeTrue();
        ring[1].X.Should().BeApproximately(10d, 1e-9d, "the original winding was already correct");
    }

    [Fact]
    public void Triangle_FormsAValidPolygon_WithTheExpectedArea()
    {
        IReadOnlyList<Coordinate> ring = LandXmlGeometry.BuildTriangleRing(
            new Coordinate(0d, 0d), new Coordinate(10d, 0d), new Coordinate(0d, 10d));

        Polygon polygon = Factory.CreatePolygon(Factory.CreateLinearRing([.. ring]));

        polygon.IsValid.Should().BeTrue();
        polygon.Area.Should().BeApproximately(50d, 1e-9d, "a right triangle with legs of 10 has area 50");
    }

    [Fact]
    public void Triangle_PreservesVertexElevations()
    {
        IReadOnlyList<Coordinate> ring = LandXmlGeometry.BuildTriangleRing(
            new CoordinateZ(0d, 0d, 10d), new CoordinateZ(10d, 0d, 12d), new CoordinateZ(0d, 10d, 14d));

        ring.Where(static c => double.IsFinite(c.Z)).Should().HaveCountGreaterThanOrEqualTo(3,
            "surface elevations are the reason a TIN exists");
    }

    [Fact]
    public void Boundary_IsClosed_WhenTheFileLeavesItOpen()
    {
        IReadOnlyList<Coordinate> open =
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 0d), new Coordinate(10d, 10d),
        ];

        IReadOnlyList<Coordinate> closed = LandXmlGeometry.CloseRing(open);

        closed.Should().HaveCount(4);
        closed[0].Equals2D(closed[^1]).Should().BeTrue();
    }

    [Fact]
    public void Boundary_AlreadyClosed_IsNotClosedTwice()
    {
        IReadOnlyList<Coordinate> alreadyClosed =
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 0d), new Coordinate(10d, 10d), new Coordinate(0d, 0d),
        ];

        LandXmlGeometry.CloseRing(alreadyClosed).Should().HaveCount(4);
    }

    [Fact]
    public void Breakline_KeepsItsVertexOrderAndElevations()
    {
        // PntList3D is northing easting elevation, repeated.
        IReadOnlyList<Coordinate> coordinates =
            LandXmlGeometry.ParseCoordinateList("100 200 5  110 210 6  120 220 7");

        LineString line = Factory.CreateLineString([.. coordinates]);

        line.NumPoints.Should().Be(3);
        line.IsClosed.Should().BeFalse("a breakline is an open edge, not a ring");
        line.Coordinates[0].X.Should().BeApproximately(200d, 1e-9d, "easting first after the swap");
        line.Coordinates[0].Y.Should().BeApproximately(100d, 1e-9d);
        line.Coordinates[2].Z.Should().BeApproximately(7d, 1e-9d);
    }

    [Fact]
    public void Breakline_TooShortToBeAnEdge_YieldsNoUsableGeometry() =>
        LandXmlGeometry.ParseCoordinateList("100 200 5").Should().HaveCount(1,
            "a single point cannot form a breakline, and the reader requires two");

    [Fact]
    public void AdjacentTriangles_ShareTheirEdgeExactly()
    {
        // Two faces of a square, split along the diagonal, must share identical vertices or the
        // rendered surface shows hairline cracks between triangles.
        Coordinate a = new(0d, 0d);
        Coordinate b = new(10d, 0d);
        Coordinate c = new(10d, 10d);
        Coordinate d = new(0d, 10d);

        Polygon first = Factory.CreatePolygon(
            Factory.CreateLinearRing([.. LandXmlGeometry.BuildTriangleRing(a, b, c)]));
        Polygon second = Factory.CreatePolygon(
            Factory.CreateLinearRing([.. LandXmlGeometry.BuildTriangleRing(a, c, d)]));

        first.IsValid.Should().BeTrue();
        second.IsValid.Should().BeTrue();
        (first.Area + second.Area).Should().BeApproximately(100d, 1e-9d,
            "the two halves must reconstitute the whole square");
        first.Intersection(second).Area.Should().BeApproximately(0d, 1e-9d,
            "triangles meet along an edge, they do not overlap");
    }
}
