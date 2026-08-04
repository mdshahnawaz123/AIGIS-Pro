namespace AiGisConverter.Domain.Enums;

/// <summary>
/// Source-native entity types the converter recognises.
/// </summary>
/// <remarks>
/// Named after CAD primitives because that is the dominant input, but readers for IFC, DGN and PDF
/// map onto the same set. A type absent here is carried as <see cref="Unknown"/> with the native
/// name preserved on the element, so an unrecognised primitive is never silently discarded.
/// </remarks>
public enum CadEntityType
{
    /// <summary>Not recognised. The native type name is retained on the element.</summary>
    Unknown = 0,

    /// <summary>A single point or node.</summary>
    Point = 1,

    /// <summary>A two-point straight segment.</summary>
    Line = 2,

    /// <summary>A connected sequence of straight segments.</summary>
    Polyline = 3,

    /// <summary>A circular arc.</summary>
    Arc = 4,

    /// <summary>A full circle.</summary>
    Circle = 5,

    /// <summary>An ellipse or elliptical arc.</summary>
    Ellipse = 6,

    /// <summary>A spline curve.</summary>
    Spline = 7,

    /// <summary>A filled or patterned region.</summary>
    Hatch = 8,

    /// <summary>Single-line text.</summary>
    Text = 9,

    /// <summary>Multi-line formatted text.</summary>
    MText = 10,

    /// <summary>An inserted block or symbol reference.</summary>
    BlockReference = 11,

    /// <summary>An attribute attached to a block reference.</summary>
    BlockAttribute = 12,

    /// <summary>A dimension annotation.</summary>
    Dimension = 13,

    /// <summary>A filled solid or 3D face.</summary>
    Solid = 14,

    /// <summary>A 3D mesh or surface.</summary>
    Mesh = 15,

    /// <summary>A point cloud or scan cluster.</summary>
    PointCloud = 16,

    /// <summary>A raster or image reference.</summary>
    Raster = 17,
}
