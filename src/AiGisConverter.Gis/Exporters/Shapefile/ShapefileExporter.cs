using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Exporters.Ogr;
using AiGisConverter.Gis.Gdal;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Exporters.Shapefile;

/// <summary>
/// Writes an ESRI Shapefile set.
/// </summary>
/// <remarks>
/// <para>
/// A Shapefile is five files, not one, and every consumer needs all of them: the <c>.shp</c>
/// geometry, the <c>.shx</c> index, the <c>.dbf</c> attributes, the <c>.prj</c> projection and the
/// <c>.cpg</c> code page. Reporting only the <c>.shp</c> as the deliverable is how a zip arrives
/// at a client missing its coordinate system.
/// </para>
/// <para>
/// The <c>.cpg</c> is written explicitly. Without it the DBF is read in the consumer's system code
/// page, and every non-ASCII place name is mangled &#8212; a defect that appears only on someone
/// else's machine.
/// </para>
/// <para>
/// The format's own limits stand regardless of what a profile asks for: 2 GB per file, ten
/// characters per field name, 254 characters per text value, and one geometry type per layer.
/// The <c>esri</c> profile exists to keep the conversion inside them.
/// </para>
/// </remarks>
public sealed class ShapefileExporter : OgrExporterBase
{
    /// <summary>Initializes a new instance of the <see cref="ShapefileExporter"/> class.</summary>
    /// <param name="environment">The native library gate.</param>
    /// <param name="crsRegistry">Supplies system definitions.</param>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the exporter.</param>
    public ShapefileExporter(
        GdalEnvironment environment,
        ICrsRegistry crsRegistry,
        IOptionsMonitor<GisOptions> options,
        ILogger<ShapefileExporter> logger)
        : base(environment, crsRegistry, options, logger)
    {
    }

    /// <inheritdoc />
    public override string FormatKey => "shapefile";

    /// <inheritdoc />
    public override ExportFormat Format => ExportFormat.Shapefile;

    /// <inheritdoc />
    public override string FileExtension => ".shp";

    /// <inheritdoc />
    public override bool SupportsMultipleLayers => false;

    /// <inheritdoc />
    protected override string DriverName => "ESRI Shapefile";

    /// <inheritdoc />
    protected override string[] GetLayerOptions(Abstractions.ExportRequest request) =>
        ["ENCODING=UTF-8", "RESIZE=YES"];

    /// <inheritdoc />
    protected override IReadOnlyList<string> GetWrittenPaths(string primaryPath) =>
    [
        primaryPath,
        Path.ChangeExtension(primaryPath, ".shx"),
        Path.ChangeExtension(primaryPath, ".dbf"),
        Path.ChangeExtension(primaryPath, ".prj"),
        Path.ChangeExtension(primaryPath, ".cpg"),
    ];
}
