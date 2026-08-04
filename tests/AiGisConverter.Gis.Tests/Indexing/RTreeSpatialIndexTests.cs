using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Indexing;
using AiGisConverter.Gis.Tests.TestSupport;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.Indexing;

public sealed class RTreeSpatialIndexTests
{
    private static RTreeSpatialIndex Grid(int size = 5, double spacing = 100d)
    {
        RTreeSpatialIndex index = new();

        for (int x = 0; x < size; x++)
        {
            for (int y = 0; y < size; y++)
            {
                index.Insert(FeatureFactory.Polygon($"f{x}-{y}", x * spacing, y * spacing));
            }
        }

        index.Build();

        return index;
    }

    private static NetTopologySuite.Geometries.Geometry Box(double x, double y, double size) =>
        FeatureFactory.Geometry.ToGeometry(new Envelope(x, x + size, y, y + size));

    [Fact]
    public void Insert_SkipsFeaturesWithoutGeometry()
    {
        RTreeSpatialIndex index = new();
        index.Insert(new GisFeature(
            "empty",
            FeatureClass.Create("X", Domain.Enums.GeometryKind.Polygon),
            null,
            [],
            LayerName.Create("L"),
            "empty"));

        index.Count.Should().Be(0);
    }

    [Fact]
    public void Insert_AfterBuild_Throws()
    {
        RTreeSpatialIndex index = Grid(2);

        Action act = () => index.Insert(FeatureFactory.Polygon("late", 0d, 0d));

        act.Should().Throw<InvalidOperationException>("STR packing produces an immutable tree");
    }

    [Fact]
    public void Extent_CoversEverythingIndexed()
    {
        RTreeSpatialIndex index = Grid(3);

        index.Extent.MinX.Should().BeApproximately(0d, 1e-9d);
        index.Extent.MaxX.Should().BeApproximately(210d, 1e-9d);
    }

    [Fact]
    public void QueryBoundingBox_ReturnsCandidatesOnly()
    {
        RTreeSpatialIndex index = Grid();

        index.QueryBoundingBox(Extent.Create(-5d, -5d, 115d, 115d))
            .Should().HaveCountGreaterThanOrEqualTo(4);
    }

    [Fact]
    public void QueryIntersects_RefinesBeyondTheBoundingBox()
    {
        // A diagonal line crosses the bounding boxes of many cells but genuinely intersects few.
        RTreeSpatialIndex index = Grid();

        NetTopologySuite.Geometries.Geometry diagonal = FeatureFactory.Geometry.CreateLineString(
            [new Coordinate(-50d, -50d), new Coordinate(500d, 500d)]);

        IReadOnlyList<GisFeature> exact = index.QueryIntersects(diagonal);
        IReadOnlyList<GisFeature> candidates = index.QueryBoundingBox(
            Extent.Create(-50d, -50d, 500d, 500d));

        exact.Should().NotBeEmpty();
        exact.Count.Should().BeLessThan(candidates.Count,
            "a bounding-box hit is not an intersection, and treating it as one is the classic bug");
    }

    [Fact]
    public void QueryContains_FindsTheEnclosingFeature()
    {
        RTreeSpatialIndex index = Grid();

        NetTopologySuite.Geometries.Geometry speck =
            FeatureFactory.Geometry.CreatePoint(new Coordinate(105d, 105d));

        index.QueryContains(speck).Should().ContainSingle();
    }

    [Fact]
    public void QueryWithin_FindsFeaturesInsideTheSearchArea()
    {
        RTreeSpatialIndex index = Grid();

        index.QueryWithin(Box(-10d, -10d, 130d)).Should().HaveCount(4,
            "the first two rows and columns fall wholly inside");
    }

    [Fact]
    public void QueryTouches_FindsEdgeContactWithoutOverlap()
    {
        RTreeSpatialIndex index = new();
        index.Insert(FeatureFactory.Polygon("a", 0d, 0d));
        index.Build();

        // The neighbouring square shares an edge at x = 10 and overlaps nowhere.
        index.QueryTouches(Box(10d, 0d, 10d)).Should().ContainSingle();
    }

    [Fact]
    public void QueryOverlaps_ExcludesContainmentAndTouching()
    {
        RTreeSpatialIndex index = new();
        index.Insert(FeatureFactory.Polygon("a", 0d, 0d));
        index.Build();

        index.QueryOverlaps(Box(5d, 5d, 10d)).Should().ContainSingle();
        index.QueryOverlaps(Box(10d, 0d, 10d)).Should().BeEmpty("touching is not overlapping");
    }

    [Fact]
    public void QueryNearest_OrdersByTrueDistanceNotEnvelopeDistance()
    {
        RTreeSpatialIndex index = Grid();

        IReadOnlyList<GisFeature> nearest = index.QueryNearest(
            FeatureFactory.Geometry.CreatePoint(new Coordinate(-100d, -100d)), 3);

        nearest.Should().HaveCount(3);
        nearest[0].Id.Should().Be("f0-0");
    }

    [Fact]
    public void QueryNearest_EmptyIndex_ReturnsNothing() =>
        new RTreeSpatialIndex().QueryNearest(
            FeatureFactory.Geometry.CreatePoint(new Coordinate(0d, 0d))).Should().BeEmpty();
}
