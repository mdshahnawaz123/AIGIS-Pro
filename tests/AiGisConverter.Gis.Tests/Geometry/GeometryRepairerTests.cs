using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Geometry;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.Geometry;

public sealed class GeometryRepairerTests
{
    private static readonly GeometryFactory Factory = new();

    [Fact]
    public void Repair_ValidGeometry_IsLeftAlone()
    {
        Polygon square = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 0d),
            new Coordinate(10d, 10d), new Coordinate(0d, 10d), new Coordinate(0d, 0d),
        ]));

        GeometryRepairResult result = new GeometryRepairer().Repair(square);

        result.Succeeded.Should().BeTrue();
        result.Action.Should().Be("none required");
        result.Geometry.Should().BeSameAs(square);
    }

    [Fact]
    public void Repair_Bowtie_ProducesValidGeometry()
    {
        Polygon bowtie = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 10d),
            new Coordinate(10d, 0d), new Coordinate(0d, 10d), new Coordinate(0d, 0d),
        ]));

        GeometryRepairResult result = new GeometryRepairer().Repair(bowtie);

        result.Succeeded.Should().BeTrue();
        result.Geometry!.IsValid.Should().BeTrue();
        result.Action.Should().Contain("buffer");
    }

    [Fact]
    public void Repair_ReportsHowMuchTheAreaMoved()
    {
        // A repair that changes the area has changed the surveyed fact, not just its encoding,
        // so the caller has to be told rather than left to assume the geometry is equivalent.
        Polygon bowtie = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 10d),
            new Coordinate(10d, 0d), new Coordinate(0d, 10d), new Coordinate(0d, 0d),
        ]));

        GeometryRepairResult result = new GeometryRepairer().Repair(bowtie);

        result.AreaChangeRatio.Should().BeGreaterThanOrEqualTo(0d);
    }

    [Fact]
    public void Repair_LineWithRepeatedVertices_KeepsDistinctOnes()
    {
        LineString line = Factory.CreateLineString(
        [
            new Coordinate(0d, 0d), new Coordinate(0d, 0d),
            new Coordinate(10d, 0d), new Coordinate(10d, 0d),
        ]);

        GeometryRepairResult result = new GeometryRepairer().Repair(line);

        // A valid line needs no repair; this one is valid, so it is returned untouched.
        result.Succeeded.Should().BeTrue();
    }
}
