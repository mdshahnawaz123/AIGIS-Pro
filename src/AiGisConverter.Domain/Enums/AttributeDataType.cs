namespace AiGisConverter.Domain.Enums;

/// <summary>
/// The storage type of a GIS attribute field.
/// </summary>
/// <remarks>
/// Deliberately narrow. It is the intersection of what Shapefile, GeoPackage, GeoJSON, CSV and KML
/// can all represent, so a schema expressed in these types survives export to any of them without
/// a lossy surprise at the last step.
/// </remarks>
public enum AttributeDataType
{
    /// <summary>Text.</summary>
    Text = 0,

    /// <summary>32-bit signed integer.</summary>
    Integer = 1,

    /// <summary>64-bit signed integer.</summary>
    Long = 2,

    /// <summary>Double-precision floating point.</summary>
    Double = 3,

    /// <summary>Boolean.</summary>
    Boolean = 4,

    /// <summary>Date and time.</summary>
    DateTime = 5,
}
