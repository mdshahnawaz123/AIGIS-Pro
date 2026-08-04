using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Reprojects geometry between coordinate reference systems.
/// </summary>
/// <remarks>
/// The domain declares the need; PROJ lives in the GIS layer behind this port. Keeping the
/// mathematics out is what allows the domain to be unit-tested with no native binaries on the
/// machine.
/// </remarks>
public interface ICoordinateTransformer
{
    /// <summary>Determines whether a transformation between two systems is available.</summary>
    /// <param name="source">The system to convert from.</param>
    /// <param name="target">The system to convert to.</param>
    /// <returns><see langword="true"/> when the transformation can be performed.</returns>
    bool CanTransform(CoordinateSystem source, CoordinateSystem target);

    /// <summary>Reprojects a geometry.</summary>
    /// <param name="geometry">The geometry to reproject.</param>
    /// <param name="source">The system the geometry is in.</param>
    /// <param name="target">The system to convert to.</param>
    /// <returns>The reprojected geometry, or a failure describing why it could not be done.</returns>
    Result<Geometry> Transform(Geometry geometry, CoordinateSystem source, CoordinateSystem target);

    /// <summary>Reprojects an extent.</summary>
    /// <param name="extent">The extent to reproject.</param>
    /// <param name="source">The system the extent is in.</param>
    /// <param name="target">The system to convert to.</param>
    /// <returns>The reprojected extent, or a failure describing why it could not be done.</returns>
    Result<Extent> Transform(Extent extent, CoordinateSystem source, CoordinateSystem target);
}
