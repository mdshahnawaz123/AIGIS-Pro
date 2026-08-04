using AiGisConverter.Cad.Geometry;
using AiGisConverter.Cad.Options;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Cad.Tests.Geometry;

public sealed class CurveTessellatorTests
{
    private static TessellationOptions Options(double tolerance = 0.01d) =>
        new() { ChordTolerance = tolerance, MinimumSegments = 4, MaximumSegments = 512 };

    [Fact]
    public void Arc_EveryPointLiesOnTheCircle()
    {
        Coordinate[] points = CurveTessellator.Arc(10d, -5d, 3d, 0.3d, 2.1d, Options());

        points.Should().HaveCountGreaterThan(2);

        foreach (Coordinate point in points)
        {
            double radius = Math.Sqrt(Math.Pow(point.X - 10d, 2d) + Math.Pow(point.Y + 5d, 2d));
            radius.Should().BeApproximately(3d, 1e-9d);
        }
    }

    [Fact]
    public void Arc_RespectsTheChordTolerance()
    {
        const double Radius = 250d;
        const double Tolerance = 0.05d;

        Coordinate[] points = CurveTessellator.Arc(0d, 0d, Radius, 0d, Math.PI, Options(Tolerance));

        for (int i = 1; i < points.Length; i++)
        {
            double chord = points[i].Distance(points[i - 1]);
            double halfAngle = Math.Asin(Math.Min(1d, chord / (2d * Radius)));
            double sagitta = Radius * (1d - Math.Cos(halfAngle));

            sagitta.Should().BeLessThanOrEqualTo(Tolerance + 1e-9d);
        }
    }

    [Fact]
    public void Circle_ProducesAnExactlyClosedRing()
    {
        Coordinate[] ring = CurveTessellator.Circle(3d, 4d, 12d, Options());

        ring[^1].Should().Be(ring[0], "floating-point drift would make NetTopologySuite reject the ring");
    }

    [Fact]
    public void Polyline_StraightSegments_KeepsOnlyTheOriginalVertices()
    {
        PolylineVertex[] vertices =
        [
            new(0d, 0d),
            new(10d, 0d),
            new(10d, 10d),
        ];

        Coordinate[] coordinates = CurveTessellator.Polyline(vertices, isClosed: false, Options());

        coordinates.Should().HaveCount(3);
        coordinates[0].X.Should().Be(0d);
        coordinates[2].Y.Should().Be(10d);
    }

    [Fact]
    public void Polyline_BulgedSegment_IsExpandedIntoAnArc()
    {
        PolylineVertex[] vertices =
        [
            new(0d, 0d, 1d),
            new(10d, 0d),
        ];

        Coordinate[] coordinates = CurveTessellator.Polyline(vertices, isClosed: false, Options());

        coordinates.Should().HaveCountGreaterThan(3,
            "a bulge describes an arc, and ignoring it silently straightens the drawing");
        coordinates[0].X.Should().BeApproximately(0d, 1e-9d);
        coordinates[^1].X.Should().BeApproximately(10d, 1e-9d);
    }

    [Fact]
    public void Polyline_DoesNotDuplicateInteriorVertices()
    {
        PolylineVertex[] vertices =
        [
            new(0d, 0d, 0.5d),
            new(10d, 0d, 0.5d),
            new(20d, 0d),
        ];

        Coordinate[] coordinates = CurveTessellator.Polyline(vertices, isClosed: false, Options());

        for (int i = 1; i < coordinates.Length; i++)
        {
            coordinates[i].Equals2D(coordinates[i - 1]).Should().BeFalse();
        }
    }

    [Fact]
    public void Polyline_Closed_ReturnsToItsStart()
    {
        PolylineVertex[] vertices =
        [
            new(0d, 0d),
            new(10d, 0d),
            new(10d, 10d),
            new(0d, 10d),
        ];

        Coordinate[] coordinates = CurveTessellator.Polyline(vertices, isClosed: true, Options());

        coordinates[^1].Equals2D(coordinates[0]).Should().BeTrue();
        coordinates.Should().HaveCount(5);
    }

    [Fact]
    public void EllipticalArc_RotationIsApplied()
    {
        // A quarter of an ellipse rotated by 90 degrees puts the major-axis endpoint on +Y.
        Coordinate[] points = CurveTessellator.EllipticalArc(
            0d, 0d, majorRadius: 10d, minorRadius: 5d,
            rotation: Math.PI / 2d, startParameter: 0d, sweep: Math.PI / 2d, Options());

        points[0].X.Should().BeApproximately(0d, 1e-9d);
        points[0].Y.Should().BeApproximately(10d, 1e-9d);
    }
}
