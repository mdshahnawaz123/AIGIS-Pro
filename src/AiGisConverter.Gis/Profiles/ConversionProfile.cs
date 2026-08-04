using System.Text.Json.Serialization;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Gis.Profiles;

/// <summary>
/// A named, self-contained description of how one organisation wants CAD data delivered.
/// </summary>
/// <remarks>
/// <para>
/// Profiles exist because "convert this drawing to GIS" means materially different things to
/// different recipients. Dubai Municipality wants a specific projection, specific layer names and
/// specific field lengths; a QGIS user wants GeoPackage in whatever the source was. Encoding that
/// as configuration rather than as code is what lets a new client be supported by an analyst
/// editing JSON instead of a developer editing a switch statement.
/// </para>
/// <para>
/// Every member is optional apart from <see cref="Id"/>. An absent value defers to
/// <see cref="Options.GisOptions"/>, so a profile states only what it actually cares about.
/// </para>
/// </remarks>
public sealed class ConversionProfile
{
    /// <summary>Gets or sets the stable identifier, for example <c>dubai-municipality</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets a one-line description.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the identifier of a profile this one extends.</summary>
    /// <remarks>
    /// Inheritance keeps client profiles short: a municipal profile states its projection and
    /// naming rules and inherits everything else, so a change to the shared defaults reaches
    /// every profile that did not deliberately override it.
    /// </remarks>
    [JsonPropertyName("extends")]
    public string? Extends { get; set; }

    /// <summary>Gets or sets the output coordinate system, for example <c>EPSG:3857</c>.</summary>
    [JsonPropertyName("outputCrs")]
    public string? OutputCrs { get; set; }

    /// <summary>Gets or sets the export format.</summary>
    [JsonPropertyName("exportFormat")]
    public ExportFormat? ExportFormat { get; set; }

    /// <summary>Gets or sets the coordinate precision, as a NetTopologySuite scale factor.</summary>
    [JsonPropertyName("precisionScale")]
    public double? PrecisionScale { get; set; }

    /// <summary>Gets or sets the chord tolerance used when tessellating curves.</summary>
    [JsonPropertyName("chordTolerance")]
    public double? ChordTolerance { get; set; }

    /// <summary>Gets or sets the Douglas-Peucker distance. Null or zero disables simplification.</summary>
    [JsonPropertyName("simplificationTolerance")]
    public double? SimplificationTolerance { get; set; }

    /// <summary>Gets or sets the source-layer to feature-class mapping.</summary>
    [JsonPropertyName("layerMapping")]
    public IDictionary<string, string> LayerMapping { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the source-attribute to output-field mapping.</summary>
    [JsonPropertyName("attributeMapping")]
    public IDictionary<string, string> AttributeMapping { get; set; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the attribute names to drop from the output.</summary>
    [JsonPropertyName("excludedAttributes")]
    public IList<string> ExcludedAttributes { get; set; } = [];

    /// <summary>Gets or sets the naming rules applied to layers and fields.</summary>
    [JsonPropertyName("naming")]
    public NamingRules Naming { get; set; } = new();

    /// <summary>Gets or sets the geometry rules.</summary>
    [JsonPropertyName("geometry")]
    public GeometryRules Geometry { get; set; } = new();

    /// <summary>Gets or sets the quality rules.</summary>
    [JsonPropertyName("qa")]
    public QualityRules Qa { get; set; } = new();

    /// <summary>Resolves the feature class for a source layer, applying the mapping then the naming rules.</summary>
    /// <param name="sourceLayer">The source layer name.</param>
    /// <returns>The output feature class name.</returns>
    public string ResolveLayerName(string sourceLayer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceLayer);

        string mapped = LayerMapping.TryGetValue(sourceLayer, out string? target) ? target : sourceLayer;

        return Naming.Apply(mapped);
    }

    /// <summary>Resolves the output field name for a source attribute.</summary>
    /// <param name="sourceAttribute">The source attribute name.</param>
    /// <returns>The output field name, or null when the attribute is excluded.</returns>
    public string? ResolveFieldName(string sourceAttribute)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceAttribute);

        if (ExcludedAttributes.Contains(sourceAttribute, StringComparer.OrdinalIgnoreCase))
        {
            return null;
        }

        string mapped = AttributeMapping.TryGetValue(sourceAttribute, out string? target)
            ? target
            : sourceAttribute;

        return Naming.Apply(mapped, Naming.MaximumFieldNameLength);
    }
}
