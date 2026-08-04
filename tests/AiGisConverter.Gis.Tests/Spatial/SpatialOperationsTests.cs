using AiGisConverter.Domain.Common;
using AiGisConverter.Gis.Spatial.Abstractions;
using AiGisConverter.Gis.Spatial.Operations;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Tests.Spatial;

public sealed class SpatialOperationsTests
{
    private static readonly GeometryFactory Factory = new();
    private static readonly SpatialOperations Operations = new(NullLogger<SpatialOperations>.Instance);

    private static Polygon Square(double x, double y, double size) =>
        Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + size, y),
            new Coordinate(x + size, y + size),
            new Coordinate(x, y + size),
            new Coordinate(x, y),
        ]));

    private static async IAsyncEnumerable<NtsGeometry> Stream(params NtsGeometry[] geometries)
    {
        foreach (NtsGeometry geometry in geometries)
        {
            yield return geometry;
            await Task.Yield();
        }
    }

    [Fact]
    public void Buffer_PositiveDistance_GrowsTheArea()
    {
        Result<NtsGeometry> result = Operations.Buffer(Square(0d, 0d, 10d), 1d);

        result.IsSuccess.Should().BeTrue();
        result.Value.Area.Should().BeGreaterThan(100d);
    }

    [Fact]
    public void Buffer_NegativeDistance_ShrinksTheArea() =>
        Operations.Buffer(Square(0d, 0d, 10d), -1d).Value.Area
            .Should().BeApproximately(64d, 0.5d, "eroding a 10x10 square by 1 leaves 8x8");

    [Fact]
    public void Buffer_FlatEndCap_ProducesLessAreaThanRound()
    {
        LineString line = Factory.CreateLineString([new Coordinate(0d, 0d), new Coordinate(10d, 0d)]);

        double round = Operations.Buffer(line, 1d, new BufferParameters(EndCap: BufferEndCap.Round)).Value.Area;
        double flat = Operations.Buffer(line, 1d, new BufferParameters(EndCap: BufferEndCap.Flat)).Value.Area;

        flat.Should().BeLessThan(round, "a flat cap omits the two semicircles");
    }

    [Fact]
    public void Buffer_NonFiniteDistance_Fails() =>
        Operations.Buffer(Square(0d, 0d, 1d), double.NaN).IsFailure.Should().BeTrue();

    [Fact]
    public void Union_TwoOverlappingSquares_MergesThem()
    {
        Result<NtsGeometry> result = Operations.Union(Square(0d, 0d, 10d), Square(5d, 0d, 10d));

        result.Value.Area.Should().BeApproximately(150d, 1e-6d, "100 + 100 minus the 50 they share");
    }

    [Fact]
    public void Union_ManyAdjacentSquares_DissolvesTheSharedEdges()
    {
        NtsGeometry[] row = [.. Enumerable.Range(0, 10).Select(i => Square(i * 10d, 0d, 10d))];

        Result<NtsGeometry> result = Operations.Union(row);

        result.Value.Area.Should().BeApproximately(1_000d, 1e-6d);
        result.Value.Should().BeOfType<Polygon>("ten adjacent squares dissolve into one polygon");
    }

    [Fact]
    public void Union_EmptyInput_ReturnsAnEmptyCollection() =>
        Operations.Union([]).Value.IsEmpty.Should().BeTrue();

    [Fact]
    public void Intersection_PartialOverlap_ReturnsTheSharedArea() =>
        Operations.Intersection(Square(0d, 0d, 10d), Square(5d, 0d, 10d)).Value.Area
            .Should().BeApproximately(50d, 1e-6d);

    [Fact]
    public void Difference_RemovesTheSubtrahend() =>
        Operations.Difference(Square(0d, 0d, 10d), Square(5d, 0d, 10d)).Value.Area
            .Should().BeApproximately(50d, 1e-6d);

    [Fact]
    public void SymmetricDifference_KeepsWhatBelongsToExactlyOne() =>
        Operations.SymmetricDifference(Square(0d, 0d, 10d), Square(5d, 0d, 10d)).Value.Area
            .Should().BeApproximately(100d, 1e-6d);

    [Fact]
    public void Dissolve_GroupsByKeyAndMergesEachGroup()
    {
        (string Key, NtsGeometry Geometry)[] parcels =
        [
            ("residential", Square(0d, 0d, 10d)),
            ("residential", Square(10d, 0d, 10d)),
            ("commercial", Square(0d, 100d, 10d)),
        ];

        Result<IReadOnlyDictionary<string, NtsGeometry>> result =
            Operations.Dissolve(parcels, static p => p.Key, static p => p.Geometry);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().HaveCount(2);
        result.Value["residential"].Area.Should().BeApproximately(200d, 1e-6d);
        result.Value["commercial"].Area.Should().BeApproximately(100d, 1e-6d);
    }

    [Fact]
    public void Dissolve_IgnoresElementsWithoutGeometry()
    {
        (string Key, NtsGeometry? Geometry)[] parcels =
        [
            ("a", Square(0d, 0d, 10d)),
            ("a", null),
        ];

        Operations.Dissolve(parcels, static p => p.Key, static p => p.Geometry)
            .Value["a"].Area.Should().BeApproximately(100d, 1e-6d);
    }

    [Fact]
    public void Merge_HomogeneousPolygons_ProduceAMultiPolygon()
    {
        Result<NtsGeometry> result = Operations.Merge([Square(0d, 0d, 10d), Square(100d, 100d, 10d)]);

        result.Value.Should().BeOfType<MultiPolygon>();
        result.Value.NumGeometries.Should().Be(2);
    }

    [Fact]
    public void Merge_PreservesPartsThatUnionWouldDissolve()
    {
        // Two adjacent squares: merge keeps two parts, union collapses them to one.
        NtsGeometry merged = Operations.Merge([Square(0d, 0d, 10d), Square(10d, 0d, 10d)]).Value;
        NtsGeometry unioned = Operations.Union([Square(0d, 0d, 10d), Square(10d, 0d, 10d)]).Value;

        merged.NumGeometries.Should().Be(2);
        unioned.NumGeometries.Should().Be(1);
    }

    [Fact]
    public void Merge_MixedTypes_ProduceAGeometryCollection()
    {
        Result<NtsGeometry> result = Operations.Merge(
        [
            Square(0d, 0d, 10d),
            Factory.CreatePoint(new Coordinate(50d, 50d)),
        ]);

        result.Value.Should().BeOfType<GeometryCollection>();
    }

    [Fact]
    public async Task ClipAsync_DropsGeometryOutsideTheBoundary()
    {
        Polygon boundary = Square(0d, 0d, 100d);

        List<NtsGeometry> clipped = [];

        await foreach (NtsGeometry geometry in Operations.ClipAsync(
            Stream(Square(10d, 10d, 10d), Square(500d, 500d, 10d)),
            boundary))
        {
            clipped.Add(geometry);
        }

        clipped.Should().ContainSingle("the second square lies wholly outside");
    }

    [Fact]
    public async Task ClipAsync_TrimsGeometryStraddlingTheBoundary()
    {
        Polygon boundary = Square(0d, 0d, 100d);

        List<NtsGeometry> clipped = [];

        await foreach (NtsGeometry geometry in Operations.ClipAsync(Stream(Square(90d, 0d, 20d)), boundary))
        {
            clipped.Add(geometry);
        }

        clipped.Should().ContainSingle();
        clipped[0].Area.Should().BeApproximately(200d, 1e-6d, "half the 20x20 square falls inside");
    }

    [Fact]
    public async Task ClipAsync_IsLazy()
    {
        int produced = 0;

        async IAsyncEnumerable<NtsGeometry> Counted()
        {
            for (int i = 0; i < 100; i++)
            {
                produced++;
                yield return Square(i * 10d, 0d, 5d);
                await Task.Yield();
            }
        }

        await using IAsyncEnumerator<NtsGeometry> enumerator =
            Operations.ClipAsync(Counted(), Square(0d, 0d, 1000d)).GetAsyncEnumerator();

        await enumerator.MoveNextAsync();

        produced.Should().Be(1, "clipping must not drain the source");
    }
}
