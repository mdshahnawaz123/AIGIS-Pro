using AiGisConverter.Gis.Abstractions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Simplify;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Geometry;

/// <summary>
/// Default <see cref="IGeometrySimplifier"/>.
/// </summary>
/// <remarks>
/// Uses topology-preserving simplification rather than plain Douglas-Peucker. Plain Douglas-Peucker
/// is faster and will happily turn two adjacent parcels into overlapping ones, or collapse a
/// narrow polygon into a self-crossing sliver. For survey data the extra cost is not optional.
/// </remarks>
public sealed class GeometrySimplifier : IGeometrySimplifier
{
    /// <inheritdoc />
    public NtsGeometry Simplify(NtsGeometry geometry, double tolerance)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (tolerance <= 0d || geometry.IsEmpty || geometry is Point or MultiPoint)
        {
            return geometry;
        }

        try
        {
            NtsGeometry simplified = TopologyPreservingSimplifier.Simplify(geometry, tolerance);

            // Simplification that empties a geometry has removed the feature, not simplified it.
            return simplified.IsEmpty ? geometry : simplified;
        }
        catch (TopologyException)
        {
            return geometry;
        }
    }
}
