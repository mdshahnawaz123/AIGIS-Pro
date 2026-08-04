using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Indexing;
using AiGisConverter.Gis.Spatial.Abstractions;
using AiGisConverter.Gis.Spatial.Analysis;
using AiGisConverter.Gis.Spatial.Query;
using AiGisConverter.Gis.Spatial.Repair;
using AiGisConverter.Gis.Spatial.Topology;
using AiGisConverter.Gis.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Tests.Spatial;

public sealed class SpatialAnalysisAndQueryTests
{
    private static readonly GeometryFactory Factory = new();

    private static ICrsRegistry Registry(bool geographic)
    {
        ICrsRegistry registry = Substitute.For<ICrsRegistry>();
        registry.IsGeographic(Arg.Any<CoordinateSystem>()).Returns(geographic);
        registry.GetWellKnownText(Arg.Any<CoordinateSystem>())
            .Returns(Result.Success("PROJCS[\"x\",UNIT[\"metre\",1]]"));

        return registry;
    }

    private static Polygon Square(double x, double y, double size) =>
        Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + size, y),
            new Coordinate(x + size, y + size),
            new Coordinate(x, y + size),
            new Coordinate(x, y),
        ]));

    [Fact]
    public void Area_ProjectedSystem_IsPlanarAndLabelledAsSuch()
    {
        SpatialAnalysis analysis = new(Registry(geographic: false));

        Result<Measurement> result = analysis.Area(Square(0d, 0d, 10d), CoordinateSystem.Create("EPSG", 27700));

        result.Value.Value.Should().BeApproximately(100d, 1e-9d);
        result.Value.IsGeodetic.Should().BeFalse();
        result.Value.AccuracyNote.Should().BeNull();
    }

    [Fact]
    public void Area_GeographicSystem_IsGeodesicNotSquareDegrees()
    {
        SpatialAnalysis analysis = new(Registry(geographic: true));

        Result<Measurement> result = analysis.Area(Square(0d, 0d, 1d), CoordinateSystem.Wgs84);

        // The planar answer would be 1.0 "square degrees", which is not an area.
        result.Value.Value.Should().BeGreaterThan(1e10d, "a one-degree cell is about 12,000 square kilometres");
        result.Value.IsGeodetic.Should().BeTrue();
        result.Value.Units.Should().Be("square metre");
        result.Value.AccuracyNote.Should().NotBeNull("the caller must know a sphere was assumed");
    }

    [Fact]
    public void Length_GeographicSystem_IsMetresNotDegrees()
    {
        SpatialAnalysis analysis = new(Registry(geographic: true));

        LineString line = Factory.CreateLineString([new Coordinate(0d, 0d), new Coordinate(1d, 0d)]);

        analysis.Length(line, CoordinateSystem.Wgs84).Value.Value
            .Should().BeApproximately(111_195d, 500d);
    }

    [Fact]
    public void Distance_GeographicSystem_IsGeodesic()
    {
        SpatialAnalysis analysis = new(Registry(geographic: true));

        Result<Measurement> result = analysis.Distance(
            Factory.CreatePoint(new Coordinate(-0.1278d, 51.5074d)),
            Factory.CreatePoint(new Coordinate(2.3522d, 48.8566d)),
            CoordinateSystem.Wgs84);

        result.Value.Value.Should().BeApproximately(343_600d, 2_000d);
    }

    [Fact]
    public void Centroid_OfACrescent_MayFallOutsideIt_ButPointOnSurfaceDoesNot()
    {
        SpatialAnalysis analysis = new(Registry(geographic: false));

        // A C-shape: the centroid sits in the gap.
        Polygon crescent = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 0d), new Coordinate(10d, 2d),
            new Coordinate(2d, 2d), new Coordinate(2d, 8d), new Coordinate(10d, 8d),
            new Coordinate(10d, 10d), new Coordinate(0d, 10d), new Coordinate(0d, 0d),
        ]));

        analysis.PointOnSurface(crescent)!.Within(crescent)
            .Should().BeTrue("label placement needs a point that is actually inside");
    }

    [Fact]
    public void BoundingBox_MatchesTheEnvelope()
    {
        SpatialAnalysis analysis = new(Registry(geographic: false));

        Extent extent = analysis.BoundingBox(Square(5d, 7d, 10d));

        extent.MinX.Should().Be(5d);
        extent.MaxY.Should().Be(17d);
    }

    [Fact]
    public void ConvexHull_OfAConcaveShape_IsConvexAndLarger()
    {
        SpatialAnalysis analysis = new(Registry(geographic: false));

        Polygon concave = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d), new Coordinate(10d, 0d), new Coordinate(5d, 5d),
            new Coordinate(10d, 10d), new Coordinate(0d, 10d), new Coordinate(0d, 0d),
        ]));

        NtsGeometry hull = analysis.ConvexHull(concave);

        hull.Area.Should().BeGreaterThan(concave.Area);
        hull.ConvexHull().Area.Should().BeApproximately(hull.Area, 1e-6d);
    }

    [Fact]
    public void BoundingBox_EmptyGeometry_IsTheEmptyExtent() =>
        new SpatialAnalysis(Registry(false)).BoundingBox(Factory.CreatePolygon()).IsEmpty.Should().BeTrue();

    private static SpatialQueryEngine Engine(bool geographic = false)
    {
        ICrsRegistry registry = Registry(geographic);

        return new SpatialQueryEngine(
            new RTreeSpatialIndex(),
            new SpatialAnalysis(registry),
            new TopologyEngine(),
            registry,
            NullLogger<SpatialQueryEngine>.Instance);
    }

    private static GisFeature[] Grid(int size = 5, double spacing = 100d) =>
        [.. from x in Enumerable.Range(0, size)
            from y in Enumerable.Range(0, size)
            select FeatureFactory.Polygon($"f{x}-{y}", x * spacing, y * spacing)];

    [Fact]
    public void QueryRadius_ReturnsOnlyFeaturesInsideTheRadius()
    {
        SpatialQueryEngine engine = Engine();
        engine.Load(Grid(), CoordinateSystem.Create("EPSG", 27700));

        IReadOnlyList<FeatureDistance> near = engine.QueryRadius(0d, 0d, 150d);

        near.Should().NotBeEmpty();
        near.Should().OnlyContain(r => r.Distance.Value <= 150d);
        near[0].Distance.Value.Should().BeLessThanOrEqualTo(near[^1].Distance.Value, "results are ordered");
    }

    [Fact]
    public void QueryRadius_ZeroRadius_FindsOnlyWhatContainsThePoint()
    {
        SpatialQueryEngine engine = Engine();
        engine.Load(Grid(), CoordinateSystem.Create("EPSG", 27700));

        engine.QueryRadius(5d, 5d, 0d).Should().ContainSingle();
    }

    [Fact]
    public void QueryNearest_OrdersByDistance()
    {
        SpatialQueryEngine engine = Engine();
        engine.Load(Grid(), CoordinateSystem.Create("EPSG", 27700));

        IReadOnlyList<FeatureDistance> nearest =
            engine.QueryNearest(Factory.CreatePoint(new Coordinate(-50d, -50d)), 3);

        nearest.Should().HaveCount(3);
        nearest[0].Feature.Id.Should().Be("f0-0");
    }

    [Theory]
    [InlineData(SpatialPredicate.Intersects)]
    [InlineData(SpatialPredicate.Within)]
    [InlineData(SpatialPredicate.Contains)]
    [InlineData(SpatialPredicate.Touches)]
    [InlineData(SpatialPredicate.Overlaps)]
    [InlineData(SpatialPredicate.Crosses)]
    [InlineData(SpatialPredicate.Disjoint)]
    public void Query_EveryPredicate_IsAnswerable(SpatialPredicate predicate)
    {
        SpatialQueryEngine engine = Engine();
        engine.Load(Grid(3), CoordinateSystem.Create("EPSG", 27700));

        Action act = () => engine.Query(Square(0d, 0d, 250d), predicate);

        act.Should().NotThrow();
    }

    [Fact]
    public void Query_Disjoint_ConsidersTheWholeSet()
    {
        SpatialQueryEngine engine = Engine();
        engine.Load(Grid(3), CoordinateSystem.Create("EPSG", 27700));

        // An R-tree cannot narrow "disjoint": the answer lies outside the search envelope.
        engine.Query(Square(0d, 0d, 15d), SpatialPredicate.Disjoint)
            .Should().HaveCount(8, "one of the nine cells intersects");
    }

    [Fact]
    public void Snapper_RemoveDuplicateVertices_DropsNearCoincidentPoints()
    {
        GeometrySnapper snapper = new();

        LineString noisy = Factory.CreateLineString(
        [
            new Coordinate(0d, 0d),
            new Coordinate(0d, 1e-9d),
            new Coordinate(10d, 0d),
        ]);

        snapper.RemoveDuplicateVertices(noisy, 1e-6d).NumPoints.Should().Be(2);
    }

    [Fact]
    public void Snapper_RemoveDuplicateVertices_WillNotDestroyARing()
    {
        GeometrySnapper snapper = new();

        // A tolerance coarse enough to collapse the ring returns the original rather than rubbish.
        NtsGeometry result = snapper.RemoveDuplicateVertices(Square(0d, 0d, 1d), 1000d);

        result.IsEmpty.Should().BeFalse();
    }

    [Fact]
    public void Snapper_ZeroTolerance_IsANoOp()
    {
        GeometrySnapper snapper = new();
        Polygon square = Square(0d, 0d, 10d);

        snapper.SnapToSelf(square, 0d).Value.Should().BeSameAs(square);
    }

    [Fact]
    public void Snapper_TooCoarseATolerance_FailsRatherThanReturningNothing()
    {
        GeometrySnapper snapper = new();

        // A 50 mm kerb line under a 100 m tolerance: the caller must be told, not handed an empty.
        Result<NtsGeometry> result = snapper.SnapToSelf(Square(0d, 0d, 0.05d), 100d);

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("too coarse");
    }
}
