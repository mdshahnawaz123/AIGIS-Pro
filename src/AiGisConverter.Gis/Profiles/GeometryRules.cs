using System.Text.Json.Serialization;

namespace AiGisConverter.Gis.Profiles;

/// <summary>Geometry expectations a profile imposes on the output.</summary>
public sealed class GeometryRules
{
    /// <summary>Gets or sets a value indicating whether single geometries are promoted to their multi form.</summary>
    /// <remarks>
    /// Shapefile has no way to mix a Polygon and a MultiPolygon in one file, so a profile targeting
    /// it promotes everything rather than splitting the layer in two.
    /// </remarks>
    [JsonPropertyName("promoteToMulti")]
    public bool PromoteToMulti { get; set; }

    /// <summary>Gets or sets a value indicating whether Z values are written.</summary>
    [JsonPropertyName("includeZ")]
    public bool IncludeZ { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether M values are written.</summary>
    [JsonPropertyName("includeM")]
    public bool IncludeM { get; set; }

    /// <summary>Gets or sets the exterior ring orientation demanded by the target format.</summary>
    [JsonPropertyName("exteriorRingOrientation")]
    public RingOrientationRule ExteriorRingOrientation { get; set; } = RingOrientationRule.CounterClockwise;

    /// <summary>Gets or sets a value indicating whether closed lines are converted to polygons.</summary>
    [JsonPropertyName("closedLinesToPolygons")]
    public bool ClosedLinesToPolygons { get; set; }

    /// <summary>Gets or sets a value indicating whether geometry collections are split into one feature each.</summary>
    [JsonPropertyName("explodeCollections")]
    public bool ExplodeCollections { get; set; } = true;
}

/// <summary>Ring orientation conventions.</summary>
/// <remarks>
/// The two mainstream formats disagree, which is the entire reason this is configurable:
/// RFC 7946 GeoJSON wants counter-clockwise exterior rings, ESRI Shapefile wants clockwise.
/// </remarks>
public enum RingOrientationRule
{
    /// <summary>Leave rings as they arrive.</summary>
    Preserve = 0,

    /// <summary>Exterior rings counter-clockwise, holes clockwise. RFC 7946.</summary>
    CounterClockwise = 1,

    /// <summary>Exterior rings clockwise, holes counter-clockwise. ESRI Shapefile.</summary>
    Clockwise = 2,
}
