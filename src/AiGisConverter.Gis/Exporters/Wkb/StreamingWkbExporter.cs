using System.Buffers.Binary;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.IO;

namespace AiGisConverter.Gis.Exporters.Wkb;

/// <summary>
/// Writes length-prefixed well-known-binary records.
/// </summary>
/// <remarks>
/// <para>
/// WKB defines the encoding of one geometry and says nothing about how to store many. This writer
/// frames each record with a little-endian 32-bit byte count, so a reader can stream the file
/// without parsing geometry to find the next boundary. The framing is documented here because it
/// is a local convention, not a standard.
/// </para>
/// <para>
/// A companion <c>.prj</c> carries the coordinate system, which WKB itself cannot express.
/// </para>
/// </remarks>
public sealed class StreamingWkbExporter : StreamingExporterBase
{
    private readonly ICrsRegistry _crsRegistry;

    /// <summary>Initializes a new instance of the <see cref="StreamingWkbExporter"/> class.</summary>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="crsRegistry">Supplies the definition for the companion projection file.</param>
    /// <param name="logger">Logger for the exporter.</param>
    public StreamingWkbExporter(
        IOptionsMonitor<GisOptions> options,
        ICrsRegistry crsRegistry,
        ILogger<StreamingWkbExporter> logger)
        : base(options, logger)
    {
        ArgumentNullException.ThrowIfNull(crsRegistry);
        _crsRegistry = crsRegistry;
    }

    /// <inheritdoc />
    public override string FormatKey => "wkb";

    /// <inheritdoc />
    public override ExportFormat Format => ExportFormat.PluginDefined;

    /// <inheritdoc />
    public override string FileExtension => ".wkb";

    /// <inheritdoc />
    protected override async Task<long> WriteCoreAsync(
        string path,
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        StreamingOptions streaming = Options.CurrentValue.Streaming;
        WKBWriter writer = new(ByteOrder.LittleEndian, handleSRID: false);

        await using FileStream stream = CreateStream(path);

        byte[] lengthBuffer = new byte[sizeof(int)];
        long written = 0;

        await foreach (GisFeature feature in features.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (feature.Geometry is null)
            {
                continue;
            }

            byte[] payload = writer.Write(feature.Geometry);
            BinaryPrimitives.WriteInt32LittleEndian(lengthBuffer, payload.Length);

            await stream.WriteAsync(lengthBuffer, cancellationToken).ConfigureAwait(false);
            await stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);

            written++;

            if (written % streaming.FlushInterval == 0)
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ReportProgress(progress, written);
        }

        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);

        Domain.Common.Result<string> definition = _crsRegistry.GetWellKnownText(request.CoordinateSystem);

        if (definition.IsSuccess)
        {
            await File.WriteAllTextAsync(
                Path.ChangeExtension(path, ".prj"),
                definition.Value,
                cancellationToken).ConfigureAwait(false);
        }

        return written;
    }

    /// <inheritdoc />
    protected override IReadOnlyList<string> GetWrittenPaths(string primaryPath) =>
        [primaryPath, Path.ChangeExtension(primaryPath, ".prj")];
}
