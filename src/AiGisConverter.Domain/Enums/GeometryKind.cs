namespace AiGisConverter.Domain.Enums;

/// <summary>
/// Coarse geometry family a CAD entity maps onto once converted to a GIS feature.
/// </summary>
public enum GeometryKind
{
    /// <summary>Geometry could not be determined.</summary>
    Unknown = 0,

    /// <summary>Zero-dimensional geometry (points, blocks, text insertion points).</summary>
    Point = 1,

    /// <summary>One-dimensional geometry (lines, polylines, arcs, splines).</summary>
    Line = 2,

    /// <summary>Two-dimensional geometry (closed polylines, hatches, solids).</summary>
    Polygon = 3,

    /// <summary>Annotation carried as an attribute rather than as geometry.</summary>
    Annotation = 4,
}
