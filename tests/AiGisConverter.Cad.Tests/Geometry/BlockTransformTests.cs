using AiGisConverter.Cad.Geometry;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Cad.Tests.Geometry;

public sealed class BlockTransformTests
{
    [Fact]
    public void Identity_LeavesCoordinatesAlone()
    {
        Coordinate result = BlockTransform.Identity.Apply(new Coordinate(3d, 4d));

        result.X.Should().BeApproximately(3d, 1e-12d);
        result.Y.Should().BeApproximately(4d, 1e-12d);
    }

    [Fact]
    public void Apply_RotatesThenTranslates()
    {
        BlockTransform transform = new(
            OriginX: 100d, OriginY: 200d, OriginZ: 0d,
            ScaleX: 1d, ScaleY: 1d, ScaleZ: 1d,
            RotationRadians: Math.PI / 2d);

        Coordinate result = transform.Apply(new Coordinate(1d, 0d));

        result.X.Should().BeApproximately(100d, 1e-9d);
        result.Y.Should().BeApproximately(201d, 1e-9d);
    }

    [Fact]
    public void Apply_HonoursNonUniformScale()
    {
        BlockTransform transform = new(0d, 0d, 0d, 2d, 3d, 1d, 0d);

        Coordinate result = transform.Apply(new Coordinate(5d, 5d));

        result.X.Should().BeApproximately(10d, 1e-9d);
        result.Y.Should().BeApproximately(15d, 1e-9d);
    }

    [Fact]
    public void Compose_SubjectsTheInnerOriginToTheOuterTransform()
    {
        // This is the detail that puts nested symbols in the wrong place when it is missed:
        // the inner insertion point is itself rotated and scaled by the outer block.
        BlockTransform outer = new(0d, 0d, 0d, 1d, 1d, 1d, Math.PI / 2d);
        BlockTransform inner = new(10d, 0d, 0d, 1d, 1d, 1d, 0d);

        BlockTransform combined = outer.Compose(inner);

        combined.OriginX.Should().BeApproximately(0d, 1e-9d);
        combined.OriginY.Should().BeApproximately(10d, 1e-9d);
    }

    [Fact]
    public void Compose_MultipliesScaleAndAddsRotation()
    {
        BlockTransform outer = new(0d, 0d, 0d, 2d, 2d, 2d, 0.5d);
        BlockTransform inner = new(0d, 0d, 0d, 3d, 3d, 3d, 0.25d);

        BlockTransform combined = outer.Compose(inner);

        combined.ScaleX.Should().BeApproximately(6d, 1e-12d);
        combined.RotationRadians.Should().BeApproximately(0.75d, 1e-12d);
    }

    [Fact]
    public void Apply_TransformsAWholeGeometry()
    {
        BlockTransform transform = new(50d, 0d, 0d, 1d, 1d, 1d, 0d);
        GeometryFactory factory = new();

        LineString line = factory.CreateLineString([new Coordinate(0d, 0d), new Coordinate(10d, 0d)]);

        NetTopologySuite.Geometries.Geometry? moved = transform.Apply(line);

        moved!.Coordinates[0].X.Should().BeApproximately(50d, 1e-9d);
        moved.Coordinates[1].X.Should().BeApproximately(60d, 1e-9d);
        line.Coordinates[0].X.Should().Be(0d, "the source geometry must not be mutated");
    }

    [Fact]
    public void Apply_NullGeometry_ReturnsNull() =>
        BlockTransform.Identity.Apply((NetTopologySuite.Geometries.Geometry?)null).Should().BeNull();
}
