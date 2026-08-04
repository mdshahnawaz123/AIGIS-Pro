using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Profiles;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Normalises a geometry into the shape the target format will accept.
/// </summary>
/// <remarks>
/// Distinct from validation: the validator decides whether a geometry is <em>correct</em>, the
/// mapper decides whether it is <em>expressible</em>. A perfectly valid Polygon is still a problem
/// in a Shapefile layer declared as MultiPolygon, and that is this type's concern.
/// </remarks>
public interface IGeometryMapper
{
    /// <summary>Maps a geometry for output.</summary>
    /// <param name="geometry">The geometry to map.</param>
    /// <param name="rules">The profile's geometry rules.</param>
    /// <returns>The mapped geometry, or a failure when it cannot be expressed.</returns>
    Result<NtsGeometry> Map(NtsGeometry geometry, GeometryRules rules);

    /// <summary>Determines the geometry family a geometry belongs to.</summary>
    /// <param name="geometry">The geometry to classify. May be null.</param>
    /// <returns>The family.</returns>
    GeometryKind Classify(NtsGeometry? geometry);

    /// <summary>Splits a geometry collection into its parts.</summary>
    /// <param name="geometry">The geometry to split.</param>
    /// <returns>The parts, or the original geometry when it is not a collection.</returns>
    IEnumerable<NtsGeometry> Explode(NtsGeometry geometry);
}

/// <summary>Reduces vertex count while keeping a geometry within a stated tolerance.</summary>
public interface IGeometrySimplifier
{
    /// <summary>Simplifies a geometry.</summary>
    /// <param name="geometry">The geometry to simplify.</param>
    /// <param name="tolerance">The maximum permitted displacement. Zero returns the input unchanged.</param>
    /// <returns>The simplified geometry.</returns>
    NtsGeometry Simplify(NtsGeometry geometry, double tolerance);
}
