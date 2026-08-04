using System.Text;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace AiGisConverter.Gis.Exporters.Wkt;

/// <summary>
/// Writes one well-known-text geometry per line.
/// </summary>
/// <remarks>
/// Geometry only, no attributes: WKT has no way to carry them. Intended for pasting into a
/// database console or a debugging session, not as a delivery format. A companion
/// <c>.prj</c> is written so the coordinates are not left unattributed.
/// </remarks>
public sealed class StreamingWktExporter : StreamingExporterBase
{
    /// <summary>Initializes a new instance of the <see cref="StreamingWktExporter"/> class.</summary>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="crsRegistry">Supplies the definition for the companion projection file.</param>
    /// <param name="logger">Logger for the exporter.</param>
    public StreamingWktExporter(
        IOptionsMonitor<GisOptions> options,
        ICrsRegistry crsRegistry,
        ILogger<StreamingWktExporter> logger)
        : base(options, logger)
    {
        ArgumentNullException.ThrowIfNull(crsRegistry);
        _crsRegistry = crsRegistry;
    }

    private readonly ICrsRegistry _crsRegistry;

    /// <inheritdoc />
    public override string FormatKey => "wkt";

    /// <inheritdoc />
    public override ExportFormat Format => ExportFormat.PluginDefined;

    /// <inheritdoc />
    public override string FileExtension => ".wkt";

    /// <inheritdoc />
    protected override async Task<long> WriteCoreAsync(
        string path,
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        StreamingOptions streaming = Options.CurrentValue.Streaming;
        WKTWriter writer = new() { OutputOrdinates = Ordinates.XYZ };

        await using FileStream stream = CreateStream(path);
        await using StreamWriter text = new(stream, new UTF8Encoding(false));

        long written = 0;

        await foreach (GisFeature feature in features.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (feature.Geometry is null)
            {
                continue;
            }

            await text.WriteLineAsync(writer.Write(feature.Geometry).AsMemory(), cancellationToken)
                .ConfigureAwait(false);

            written++;

            if (written % streaming.FlushInterval == 0)
            {
                await text.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ReportProgress(progress, written);
        }

        await text.FlushAsync(cancellationToken).ConfigureAwait(false);
        await WriteProjectionFileAsync(path, request, cancellationToken).ConfigureAwait(false);

        return written;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<string> GetWrittenPaths(string primaryPath) =>
        [primaryPath, Path.ChangeExtension(primaryPath, ".prj")];

    private async Task WriteProjectionFileAsync(
        string path,
        ExportRequest request,
        CancellationToken cancellationToken)
    {
        Domain.Common.Result<string> definition = _crsRegistry.GetWellKnownText(request.CoordinateSystem);

        if (definition.IsFailure)
        {
            Logger.LogWarning(
                "No definition available for {Crs}; the companion projection file was not written.",
                request.CoordinateSystem.Identifier);

            return;
        }

        await File.WriteAllTextAsync(
            Path.ChangeExtension(path, ".prj"),
            definition.Value,
            cancellationToken).ConfigureAwait(false);
    }
}
