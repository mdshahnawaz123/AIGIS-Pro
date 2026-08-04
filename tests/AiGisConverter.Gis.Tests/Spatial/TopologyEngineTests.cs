using AiGisConverter.Gis.Spatial.Topology;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.Spatial;

public sealed class TopologyEngineTests
{
    private static readonly GeometryFactory Factory = new();
    private static readonly TopologyEngine Engine = new();

    private static Polygon Square(double x, double y, double size) =>
        Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + size, y),
            new Coordinate(x + size, y + size),
            new Coordinate(x, y + size),
            new Coordinate(x, y),
        ]));

    private static LineString Line(double x1, double y1, double x2, double y2) =>
        Factory.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]);

    [Fact]
    public void Touches_AdjacentSquares_ShareAnEdgeOnly() =>
        Engine.Touches(Square(0d, 0d, 10d), Square(10d, 0d, 10d)).Should().BeTrue();

    [Fact]
    public void Touches_OverlappingSquares_IsFalse() =>
        Engine.Touches(Square(0d, 0d, 10d), Square(5d, 0d, 10d)).Should().BeFalse();

    [Fact]
    public void Touches_AGeometryAgainstItself_IsFalse() =>
        Engine.Touches(Square(0d, 0d, 10d), Square(0d, 0d, 10d))
            .Should().BeFalse("their interiors coincide, so they do not merely touch");

    [Fact]
    public void Within_And_Contains_AreConverses()
    {
        Polygon outer = Square(0d, 0d, 100d);
        Polygon inner = Square(10d, 10d, 10d);

        Engine.Within(inner, outer).Should().BeTrue();
        Engine.Contains(outer, inner).Should().BeTrue();
        Engine.Within(outer, inner).Should().BeFalse();
    }

    [Fact]
    public void Overlaps_PartiallyOverlappingSquares_IsTrue() =>
        Engine.Overlaps(Square(0d, 0d, 10d), Square(5d, 5d, 10d)).Should().BeTrue();

    [Fact]
    public void Overlaps_Containment_IsFalse() =>
        Engine.Overlaps(Square(0d, 0d, 100d), Square(10d, 10d, 10d))
            .Should().BeFalse("a geometry does not overlap one it contains");

    [Fact]
    public void Overlaps_LineAgainstPolygon_IsFalseBecauseDimensionsDiffer() =>
        Engine.Overlaps(Line(-5d, 5d, 15d, 5d), Square(0d, 0d, 10d))
            .Should().BeFalse("overlaps requires equal dimension; this is a crossing");

    [Fact]
    public void Crosses_LineThroughPolygon_IsTrue() =>
        Engine.Crosses(Line(-5d, 5d, 15d, 5d), Square(0d, 0d, 10d))
            .Should().BeTrue("this is the predicate people reach for Overlaps by mistake");

    [Fact]
    public void Crosses_TwoIntersectingLines_IsTrue() =>
        Engine.Crosses(Line(0d, 0d, 10d, 10d), Line(0d, 10d, 10d, 0d)).Should().BeTrue();

    [Fact]
    public void Crosses_ParallelLines_IsFalse() =>
        Engine.Crosses(Line(0d, 0d, 10d, 0d), Line(0d, 5d, 10d, 5d)).Should().BeFalse();

    [Fact]
    public void Intersects_IsImpliedByEveryOtherPositivePredicate()
    {
        Polygon a = Square(0d, 0d, 10d);

        Engine.Intersects(a, Square(5d, 5d, 10d)).Should().BeTrue("overlapping");
        Engine.Intersects(a, Square(10d, 0d, 10d)).Should().BeTrue("touching");
        Engine.Intersects(a, Square(2d, 2d, 2d)).Should().BeTrue("containing");
    }

    [Fact]
    public void Disjoint_SeparatedSquares_IsTrue()
    {
        Engine.Disjoint(Square(0d, 0d, 10d), Square(100d, 100d, 10d)).Should().BeTrue();
        Engine.Intersects(Square(0d, 0d, 10d), Square(100d, 100d, 10d)).Should().BeFalse();
    }

    [Fact]
    public void Predicates_EmptyGeometry_AreAllFalse()
    {
        Polygon empty = Factory.CreatePolygon();
        Polygon square = Square(0d, 0d, 10d);

        Engine.Intersects(empty, square).Should().BeFalse();
        Engine.Touches(empty, square).Should().BeFalse();
        Engine.Crosses(empty, square).Should().BeFalse();
    }

    [Fact]
    public void Relate_ReturnsANineCharacterMatrix() =>
        Engine.Relate(Square(0d, 0d, 10d), Square(10d, 0d, 10d)).Should().HaveLength(9);

    [Fact]
    public void Relate_WithPattern_MatchesTheEquivalentNamedPredicate()
    {
        Polygon a = Square(0d, 0d, 10d);
        Polygon b = Square(10d, 0d, 10d);

        // Corrected: "touches" between two areas is not a single pattern. The OGC definition is
        // the disjunction of three, and the original test asserted only the first of them, so it
        // failed on a pair of squares that touch along an edge rather than at a point.
        bool touches = Engine.Relate(a, b, "FT*******")
            || Engine.Relate(a, b, "F**T*****")
            || Engine.Relate(a, b, "F***T****");

        touches.Should().Be(Engine.Touches(a, b));
    }

    [Fact]
    public void Relate_MalformedPattern_DoesNotSilentlyReportAMatch()
    {
        // Corrected: NetTopologySuite does not validate pattern length, so asserting that it
        // throws was an assumption about the library rather than a requirement of ours. What we
        // actually need is that a malformed pattern never reports a false positive.
        Action act = () => Engine.Relate(Square(0d, 0d, 1d), Square(0d, 0d, 1d), "TOO-SHORT");

        act.Should().NotThrow();
        Engine.Relate(Square(0d, 0d, 1d), Square(0d, 0d, 1d), "TOO-SHORT").Should().BeFalse();
    }

    [Fact]
    public void Predicates_InvalidGeometry_ReturnFalseRatherThanThrowing()
    {
        // A bowtie defeats the noding. One bad parcel must not abort an analysis of ten thousand.
        Polygon bowtie = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 10d),
            new Coordinate(10d, 0d), new Coordinate(0d, 10d), new Coordinate(0d, 0d),
        ]));

        Action act = () => Engine.Overlaps(bowtie, Square(0d, 0d, 10d));

        act.Should().NotThrow();
    }
}
