using AiGisConverter.Cad.Geometry;

namespace AiGisConverter.Cad.Tests.Geometry;

public sealed class BulgeArcTests
{
    [Fact]
    public void TryCreate_UnitBulge_ProducesSemicircleCentredOnChordMidpoint()
    {
        bool created = BulgeArc.TryCreate(0d, 0d, 1d, 0d, bulge: 1d, out BulgeArc arc);

        created.Should().BeTrue();
        arc.Radius.Should().BeApproximately(0.5d, 1e-12d);
        arc.CentreX.Should().BeApproximately(0.5d, 1e-12d);
        arc.CentreY.Should().BeApproximately(0d, 1e-12d);
        arc.Sweep.Should().BeApproximately(Math.PI, 1e-12d);
    }

    [Fact]
    public void TryCreate_KnownBulge_MatchesChordRadiusRelation()
    {
        // b = 0.5 over a chord of 2 implies r = 1.25, checked against c = 2r sin(theta/2).
        BulgeArc.TryCreate(0d, 0d, 2d, 0d, bulge: 0.5d, out BulgeArc arc).Should().BeTrue();

        arc.Radius.Should().BeApproximately(1.25d, 1e-12d);

        double chord = 2d * arc.Radius * Math.Sin(Math.Abs(arc.Sweep) / 2d);
        chord.Should().BeApproximately(2d, 1e-12d);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(1e-12d)]
    [InlineData(-1e-12d)]
    public void TryCreate_NegligibleBulge_IsTreatedAsStraight(double bulge) =>
        BulgeArc.TryCreate(0d, 0d, 10d, 0d, bulge, out _).Should().BeFalse();

    [Fact]
    public void TryCreate_ZeroLengthChord_IsRejected() =>
        BulgeArc.TryCreate(5d, 5d, 5d, 5d, bulge: 0.5d, out _).Should().BeFalse();

    [Fact]
    public void TryCreate_PositiveBulge_SweepsCounterClockwise()
    {
        BulgeArc.TryCreate(0d, 0d, 1d, 0d, bulge: 0.4d, out BulgeArc positive).Should().BeTrue();
        BulgeArc.TryCreate(0d, 0d, 1d, 0d, bulge: -0.4d, out BulgeArc negative).Should().BeTrue();

        positive.Sweep.Should().BePositive();
        negative.Sweep.Should().BeNegative();
    }

    [Theory]
    [InlineData(0.05d)]
    [InlineData(0.5d)]
    [InlineData(1d)]
    [InlineData(2.5d)]
    [InlineData(-0.3d)]
    [InlineData(-1.7d)]
    public void TryCreate_ArcAlwaysTerminatesAtTheSecondVertex(double bulge)
    {
        const double StartX = 1234.5d;
        const double StartY = -987.25d;
        const double EndX = 1300.75d;
        const double EndY = -900.5d;

        BulgeArc.TryCreate(StartX, StartY, EndX, EndY, bulge, out BulgeArc arc).Should().BeTrue();

        double endAngle = arc.StartAngle + arc.Sweep;
        double x = arc.CentreX + (arc.Radius * Math.Cos(endAngle));
        double y = arc.CentreY + (arc.Radius * Math.Sin(endAngle));

        x.Should().BeApproximately(EndX, 1e-9d);
        y.Should().BeApproximately(EndY, 1e-9d);
    }
}
