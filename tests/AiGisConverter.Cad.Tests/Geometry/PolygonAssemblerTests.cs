using AiGisConverter.Cad.Geometry;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Cad.Tests.Geometry;

public sealed class PolygonAssemblerTests
{
    private static Coordinate[] Square(double x, double y, double size) =>
    [
        new(x, y),
        new(x + size, y),
        new(x + size, y + size),
        new(x, y + size),
    ];

    [Fact]
    public void TryCloseRing_UnclosedInput_IsClosed()
    {
        PolygonAssembler.TryCloseRing(Square(0d, 0d, 10d), out LinearRing? ring).Should().BeTrue();

        ring!.IsClosed.Should().BeTrue();
        ring.NumPoints.Should().Be(5);
    }

    [Fact]
    public void TryCloseRing_RemovesConsecutiveDuplicates()
    {
        Coordinate[] withDuplicates =
        [
            new(0d, 0d),
            new(0d, 0d),
            new(10d, 0d),
            new(10d, 10d),
            new(10d, 10d),
        ];

        PolygonAssembler.TryCloseRing(withDuplicates, out LinearRing? ring).Should().BeTrue();
        ring!.NumPoints.Should().Be(4);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void TryCloseRing_TooFewPoints_IsRejected(int count)
    {
        Coordinate[] coordinates = [.. Enumerable.Range(0, count).Select(i => new Coordinate(i, i))];

        PolygonAssembler.TryCloseRing(coordinates, out _).Should().BeFalse();
    }

    [Fact]
    public void TryCloseRing_CollinearPoints_IsRejected()
    {
        Coordinate[] collinear = [new(0d, 0d), new(5d, 0d), new(10d, 0d)];

        PolygonAssembler.TryCloseRing(collinear, out _).Should().BeFalse(
            "a zero-area ring is not a boundary");
    }

    [Fact]
    public void Assemble_SingleRing_ProducesAPolygon()
    {
        NetTopologySuite.Geometries.Geometry? geometry =
            PolygonAssembler.Assemble([Square(0d, 0d, 10d)]);

        geometry.Should().BeOfType<Polygon>();
        geometry!.Area.Should().BeApproximately(100d, 1e-9d);
    }

    [Fact]
    public void Assemble_NestedRing_IsTreatedAsAHole()
    {
        // The island flags in real DXF files are frequently wrong, so containment decides.
        NetTopologySuite.Geometries.Geometry? geometry = PolygonAssembler.Assemble(
        [
            Square(0d, 0d, 10d),
            Square(3d, 3d, 4d),
        ]);

        geometry.Should().BeOfType<Polygon>();

        Polygon polygon = (Polygon)geometry!;
        polygon.NumInteriorRings.Should().Be(1);
        polygon.Area.Should().BeApproximately(100d - 16d, 1e-9d);
    }

    /// <remarks>
    /// This case passes under both the correct and the broken containment test, which is why it
    /// gave no warning: disjoint rings do not share an interior point, so the point-based depth
    /// scan happens to get the right answer. Only concentric rings expose the defect.
    /// </remarks>
    [Fact]
    public void Assemble_TwoDisjointRings_ProducesAMultiPolygon()
    {
        NetTopologySuite.Geometries.Geometry? geometry = PolygonAssembler.Assemble(
        [
            Square(0d, 0d, 10d),
            Square(100d, 100d, 5d),
        ]);

        geometry.Should().BeOfType<MultiPolygon>();
        geometry!.Area.Should().BeApproximately(125d, 1e-9d);
    }

    [Fact]
    public void Assemble_IslandWithinAHole_IsSolidAgain()
    {
        // Depth 0 = outer, 1 = hole, 2 = island. An even depth is always solid.
        NetTopologySuite.Geometries.Geometry? geometry = PolygonAssembler.Assemble(
        [
            Square(0d, 0d, 20d),
            Square(4d, 4d, 12d),
            Square(7d, 7d, 6d),
        ]);

        geometry!.Area.Should().BeApproximately(400d - 144d + 36d, 1e-9d);
    }

    [Fact]
    public void Assemble_NoUsableRings_ReturnsNull() =>
        PolygonAssembler.Assemble([[new Coordinate(0d, 0d)]]).Should().BeNull();
}
