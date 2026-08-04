using AiGisConverter.Plugins.LandXml;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Plugins.LandXml.Tests;

/// <summary>
/// Coordinate-order and curve tests.
/// </summary>
/// <remarks>
/// The northing-first ordering is the single highest-consequence detail in this reader: getting it
/// wrong mirrors every site about the 45 degree line and nothing throws. It is asserted directly.
/// </remarks>
public sealed class LandXmlGeometryTests
{
    [Fact]
    public void Coordinate_IsReadNorthingFirst_AndSwappedToEastingFirst()
    {
        // LandXML writes "northing easting elevation".
        LandXmlGeometry.TryParseCoordinate("2762128.5 483288.25 12.75", out Coordinate coordinate)
            .Should().BeTrue();

        coordinate.X.Should().BeApproximately(483288.25d, 1e-6d, "x is the easting");
        coordinate.Y.Should().BeApproximately(2762128.5d, 1e-6d, "y is the northing");
        coordinate.Z.Should().BeApproximately(12.75d, 1e-6d);
    }

    [Fact]
    public void Coordinate_WithoutElevation_IsStillRead()
    {
        LandXmlGeometry.TryParseCoordinate("100.0 200.0", out Coordinate coordinate).Should().BeTrue();

        coordinate.X.Should().BeApproximately(200d, 1e-9d);
        coordinate.Y.Should().BeApproximately(100d, 1e-9d);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("only-one-value")]
    [InlineData("not a number")]
    public void Coordinate_Malformed_IsRejectedRatherThanGuessed(string text) =>
        LandXmlGeometry.TryParseCoordinate(text, out _).Should().BeFalse();

    [Fact]
    public void CoordinateList_ReadsFlatTriples()
    {
        IReadOnlyList<Coordinate> coordinates =
            LandXmlGeometry.ParseCoordinateList("100 200 5  110 210 6  120 220 7");

        coordinates.Should().HaveCount(3);
        coordinates[0].X.Should().BeApproximately(200d, 1e-9d);
        coordinates[2].Y.Should().BeApproximately(120d, 1e-9d);
        coordinates[2].Z.Should().BeApproximately(7d, 1e-9d);
    }

    [Fact]
    public void Arc_IsTessellated_NotReducedToItsChord()
    {
        // Quarter circle, radius 10, centred on the origin, from (10,0) to (0,10).
        Coordinate start = new(10d, 0d);
        Coordinate centre = new(0d, 0d);
        Coordinate end = new(0d, 10d);

        IReadOnlyList<Coordinate> arc = LandXmlGeometry.TessellateArc(start, centre, end, clockwise: false);

        arc.Should().HaveCountGreaterThan(2, "an arc reduced to its chord loses real area");
        arc[0].Should().Be(start);
        arc[^1].Should().Be(end);

        foreach (Coordinate point in arc)
        {
            double radius = Math.Sqrt((point.X * point.X) + (point.Y * point.Y));
            radius.Should().BeApproximately(10d, 1e-6d, "every tessellated point lies on the arc");
        }
    }

    [Fact]
    public void Arc_Direction_IsHonoured()
    {
        Coordinate start = new(10d, 0d);
        Coordinate centre = new(0d, 0d);
        Coordinate end = new(0d, 10d);

        IReadOnlyList<Coordinate> counterClockwise =
            LandXmlGeometry.TessellateArc(start, centre, end, clockwise: false);
        IReadOnlyList<Coordinate> clockwise =
            LandXmlGeometry.TessellateArc(start, centre, end, clockwise: true);

        // The short way round is a quarter turn; the long way is three quarters.
        clockwise.Count.Should().BeGreaterThan(counterClockwise.Count);

        counterClockwise.Should().Contain(c => c.X > 0d && c.Y > 0d, "the short arc passes through the first quadrant");
        clockwise.Should().Contain(c => c.X < 0d, "the long arc sweeps behind the centre");
    }

    [Fact]
    public void AppendWithoutDuplicate_DoesNotRepeatTheJoinBetweenSegments()
    {
        List<Coordinate> path = [new Coordinate(0d, 0d), new Coordinate(10d, 0d)];

        LandXmlGeometry.AppendWithoutDuplicate(path, [new Coordinate(10d, 0d), new Coordinate(10d, 10d)]);

        path.Should().HaveCount(3, "the shared endpoint of two segments is stored once");
        path[^1].X.Should().BeApproximately(10d, 1e-9d);
        path[^1].Y.Should().BeApproximately(10d, 1e-9d);
    }
}
