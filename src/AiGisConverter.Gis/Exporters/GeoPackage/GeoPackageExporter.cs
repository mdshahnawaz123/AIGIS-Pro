using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Exporters.Ogr;
using AiGisConverter.Gis.Gdal;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Exporters.GeoPackage;

/// <summary>
/// Writes an OGC GeoPackage.
/// </summary>
/// <remarks>
/// <para>
/// The format of choice for anything non-trivial: one file, no name-length limits, no field-count
/// limits, real types including dates and booleans, several layers with different geometry types,
/// and a spatial index. It is SQLite underneath, which is also why transaction batching matters so
/// much here &#8212; an untransacted insert per feature is roughly two orders of magnitude slower.
/// </para>
/// <para>
/// The spatial index is created after the data is loaded rather than maintained during it.
/// Building an R-tree incrementally over a million inserts costs far more than building it once at
/// the end.
/// </para>
/// </remarks>
public sealed class GeoPackageExporter : OgrExporterBase
{
    /// <summary>Initializes a new instance of the <see cref="GeoPackageExporter"/> class.</summary>
    /// <param name="environment">The native library gate.</param>
    /// <param name="crsRegistry">Supplies system definitions.</param>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the exporter.</param>
    public GeoPackageExporter(
        GdalEnvironment environment,
        ICrsRegistry crsRegistry,
        IOptionsMonitor<GisOptions> options,
        ILogger<GeoPackageExporter> logger)
        : base(environment, crsRegistry, options, logger)
    {
    }

    /// <inheritdoc />
    public override string FormatKey => "geopackage";

    /// <inheritdoc />
    public override ExportFormat Format => ExportFormat.GeoPackage;

    /// <inheritdoc />
    public override string FileExtension => ".gpkg";

    /// <inheritdoc />
    public override bool SupportsMultipleLayers => true;

    /// <inheritdoc />
    protected override string DriverName => "GPKG";

    /// <inheritdoc />
    protected override string[] GetLayerOptions(Abstractions.ExportRequest request) =>
    [
        "GEOMETRY_NAME=geom",
        "FID=fid",
        "SPATIAL_INDEX=YES",
    ];
}
