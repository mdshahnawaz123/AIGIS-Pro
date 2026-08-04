using AiGisConverter.Gis.Geometry;
using AiGisConverter.Gis.Profiles;
using NetTopologySuite.Algorithm;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.Geometry;

public sealed class RingOrientationNormaliserTests
{
    private static readonly GeometryFactory Factory = new();

    private static Polygon ClockwiseSquareWithHole()
    {
        LinearRing shell = Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(0d, 10d),
            new Coordinate(10d, 10d), new Coordinate(10d, 0d), new Coordinate(0d, 0d),
        ]);

        LinearRing hole = Factory.CreateLinearRing(
        [
            new Coordinate(3d, 3d), new Coordinate(3d, 6d),
            new Coordinate(6d, 6d), new Coordinate(6d, 3d), new Coordinate(3d, 3d),
        ]);

        return Factory.CreatePolygon(shell, [hole]);
    }

    [Fact]
    public void Normalise_CounterClockwiseRule_MatchesRfc7946()
    {
        Polygon result = (Polygon)RingOrientationNormaliser.Normalise(
            ClockwiseSquareWithHole(), RingOrientationRule.CounterClockwise);

        Orientation.IsCCW(result.ExteriorRing.Coordinates).Should().BeTrue();
        Orientation.IsCCW(result.GetInteriorRingN(0).Coordinates).Should().BeFalse(
            "holes always wind opposite to the shell");
    }

    [Fact]
    public void Normalise_ClockwiseRule_MatchesEsriShapefile()
    {
        Polygon result = (Polygon)RingOrientationNormaliser.Normalise(
            ClockwiseSquareWithHole(), RingOrientationRule.Clockwise);

        Orientation.IsCCW(result.ExteriorRing.Coordinates).Should().BeFalse();
        Orientation.IsCCW(result.GetInteriorRingN(0).Coordinates).Should().BeTrue();
    }

    [Fact]
    public void Normalise_PreserveRule_ReturnsTheInputUntouched()
    {
        Polygon input = ClockwiseSquareWithHole();

        RingOrientationNormaliser.Normalise(input, RingOrientationRule.Preserve).Should().BeSameAs(input);
    }

    [Fact]
    public void Normalise_DoesNotChangeTheArea()
    {
        Polygon input = ClockwiseSquareWithHole();

        NetTopologySuite.Geometries.Geometry result =
            RingOrientationNormaliser.Normalise(input, RingOrientationRule.CounterClockwise);

        result.Area.Should().BeApproximately(input.Area, 1e-9d, "reversing a ring must not move a boundary");
    }
}
