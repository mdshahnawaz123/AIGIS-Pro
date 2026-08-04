namespace AiGisConverter.Domain.Enums;

/// <summary>
/// Output formats the converter can write. Extended by export plugins, which contribute their own
/// <see cref="Abstractions.Services.IFeatureExporter"/> and advertise a format key of their own.
/// </summary>
public enum ExportFormat
{
    /// <summary>Format not specified.</summary>
    Unspecified = 0,

    /// <summary>ESRI Shapefile.</summary>
    Shapefile = 1,

    /// <summary>GeoJSON.</summary>
    GeoJson = 2,

    /// <summary>OGC GeoPackage.</summary>
    GeoPackage = 3,

    /// <summary>Comma-separated values with optional WKT geometry.</summary>
    Csv = 4,

    /// <summary>Keyhole Markup Language.</summary>
    Kml = 5,

    /// <summary>A format contributed by a plugin. The concrete key is carried by the exporter.</summary>
    PluginDefined = 100,
}
