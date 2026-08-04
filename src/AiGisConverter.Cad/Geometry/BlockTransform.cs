using NetTopologySuite.Geometries;

namespace AiGisConverter.Cad.Geometry;

/// <summary>
/// The placement of a block reference: translation, non-uniform scale and rotation.
/// </summary>
/// <remarks>
/// Composable, so a block nested three deep is handled by folding the transforms rather than by
/// recursion that re-derives coordinates at each level.
/// </remarks>
/// <param name="OriginX">Insertion point X.</param>
/// <param name="OriginY">Insertion point Y.</param>
/// <param name="OriginZ">Insertion point Z.</param>
/// <param name="ScaleX">X scale factor.</param>
/// <param name="ScaleY">Y scale factor.</param>
/// <param name="ScaleZ">Z scale factor.</param>
/// <param name="RotationRadians">Rotation about the insertion point.</param>
public readonly record struct BlockTransform(
    double OriginX,
    double OriginY,
    double OriginZ,
    double ScaleX,
    double ScaleY,
    double ScaleZ,
    double RotationRadians)
{
    /// <summary>Gets the transform that changes nothing.</summary>
    public static BlockTransform Identity { get; } = new(0d, 0d, 0d, 1d, 1d, 1d, 0d);

    /// <summary>Applies the transform to a coordinate.</summary>
    /// <param name="coordinate">The coordinate to transform.</param>
    /// <returns>The transformed coordinate.</returns>
    public Coordinate Apply(Coordinate coordinate)
    {
        ArgumentNullException.ThrowIfNull(coordinate);

        double cos = Math.Cos(RotationRadians);
        double sin = Math.Sin(RotationRadians);

        double scaledX = coordinate.X * ScaleX;
        double scaledY = coordinate.Y * ScaleY;

        double x = OriginX + (scaledX * cos) - (scaledY * sin);
        double y = OriginY + (scaledX * sin) + (scaledY * cos);

        if (double.IsNaN(coordinate.Z))
        {
            return new Coordinate(x, y);
        }

        return new CoordinateZ(x, y, OriginZ + (coordinate.Z * ScaleZ));
    }

    /// <summary>Applies the transform to a whole geometry.</summary>
    /// <param name="geometry">The geometry to transform. May be null.</param>
    /// <returns>A transformed copy, or null when the input was null.</returns>
    public NetTopologySuite.Geometries.Geometry? Apply(NetTopologySuite.Geometries.Geometry? geometry)
    {
        if (geometry is null)
        {
            return null;
        }

        NetTopologySuite.Geometries.Geometry copy = geometry.Copy();
        copy.Apply(new CoordinateTransformFilter(this));
        copy.GeometryChanged();

        return copy;
    }

    /// <summary>
    /// Composes this transform with an inner one, as when a block contains another block.
    /// </summary>
    /// <remarks>
    /// The inner origin is itself subject to the outer rotation and scale, which is the detail that
    /// makes nested symbols land in the wrong place when it is missed.
    /// </remarks>
    /// <param name="inner">The transform applied first.</param>
    /// <returns>The combined transform.</returns>
    public BlockTransform Compose(BlockTransform inner)
    {
        Coordinate innerOrigin = Apply(new CoordinateZ(inner.OriginX, inner.OriginY, inner.OriginZ));

        return new BlockTransform(
            innerOrigin.X,
            innerOrigin.Y,
            innerOrigin.Z,
            ScaleX * inner.ScaleX,
            ScaleY * inner.ScaleY,
            ScaleZ * inner.ScaleZ,
            RotationRadians + inner.RotationRadians);
    }

    private sealed class CoordinateTransformFilter : ICoordinateSequenceFilter
    {
        private readonly BlockTransform _transform;

        public CoordinateTransformFilter(BlockTransform transform) => _transform = transform;

        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence sequence, int index)
        {
            Coordinate transformed = _transform.Apply(sequence.GetCoordinate(index));

            sequence.SetX(index, transformed.X);
            sequence.SetY(index, transformed.Y);

            if (sequence.HasZ && !double.IsNaN(transformed.Z))
            {
                sequence.SetZ(index, transformed.Z);
            }
        }
    }
}
