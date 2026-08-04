using AiGisConverter.Cad.Geometry;
using AiGisConverter.Cad.Options;

namespace AiGisConverter.Cad.Tests.Geometry;

public sealed class CurveTessellationTests
{
    private static TessellationOptions Options(double tolerance = 0.01d) =>
        new() { ChordTolerance = tolerance, MinimumSegments = 4, MaximumSegments = 512 };

    [Fact]
    public void SegmentCountFor_LargeRadius_UsesMoreSegmentsThanSmallRadius()
    {
        int large = CurveTessellation.SegmentCountFor(500d, 2d * Math.PI, Options());
        int small = CurveTessellation.SegmentCountFor(0.5d, 2d * Math.PI, Options());

        large.Should().BeGreaterThan(small,
            "a fixed segment count is visibly polygonal on a highway curve and wasteful on a fillet");
    }

    [Fact]
    public void SegmentCountFor_ToleranceExceedingRadius_FallsBackToTheMinimum() =>
        CurveTessellation.SegmentCountFor(0.001d, 2d * Math.PI, Options()).Should().Be(4);

    [Fact]
    public void SegmentCountFor_IsClampedToTheMaximum() =>
        CurveTessellation.SegmentCountFor(1e9d, 2d * Math.PI, Options(1e-9d)).Should().Be(512);

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    public void SegmentCountFor_DegenerateRadius_FallsBackToTheMinimum(double radius) =>
        CurveTessellation.SegmentCountFor(radius, Math.PI, Options()).Should().Be(4);

    [Fact]
    public void CounterClockwiseSweep_EqualAngles_IsAFullTurn() =>
        CurveTessellation.CounterClockwiseSweep(1d, 1d)
            .Should().BeApproximately(2d * Math.PI, 1e-12d,
                "DXF stores a closed circular arc with equal start and end angles");

    [Fact]
    public void CounterClockwiseSweep_WrapsThroughZero() =>
        CurveTessellation.CounterClockwiseSweep(3d * Math.PI / 2d, Math.PI / 2d)
            .Should().BeApproximately(Math.PI, 1e-12d);

    [Theory]
    [InlineData(-7d * Math.PI, Math.PI)]
    [InlineData(4d * Math.PI, 0d)]
    public void NormaliseAngle_MapsIntoZeroToTwoPi(double input, double expected) =>
        CurveTessellation.NormaliseAngle(input).Should().BeApproximately(expected, 1e-12d);
}
