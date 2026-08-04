using System.Text;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;

namespace AiGisConverter.Gis.Exporters.Csv;

/// <summary>
/// Writes RFC 4180 CSV with a WKT geometry column.
/// </summary>
/// <remarks>
/// <para>
/// A UTF-8 byte-order mark is written deliberately. Without it Excel opens the file in the system
/// ANSI code page and mangles every non-ASCII place name, which is the single most common
/// complaint about CSV deliverables.
/// </para>
/// <para>
/// Text values that look numeric but are not &#8212; plot references with leading zeros &#8212;
/// are still written as bare text. Excel will reinterpret them on open whatever this writer does;
/// the fix is to import rather than open, and the QA report says so.
/// </para>
/// </remarks>
public sealed class StreamingCsvExporter : StreamingExporterBase
{
    private const string GeometryColumn = "WKT";

    /// <summary>Initializes a new instance of the <see cref="StreamingCsvExporter"/> class.</summary>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the exporter.</param>
    public StreamingCsvExporter(IOptionsMonitor<GisOptions> options, ILogger<StreamingCsvExporter> logger)
        : base(options, logger)
    {
    }

    /// <inheritdoc />
    public override string FormatKey => "csv";

    /// <inheritdoc />
    public override ExportFormat Format => ExportFormat.Csv;

    /// <inheritdoc />
    public override string FileExtension => ".csv";

    /// <inheritdoc />
    protected override async Task<long> WriteCoreAsync(
        string path,
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        StreamingOptions streaming = Options.CurrentValue.Streaming;
        WKTWriter wkt = new() { OutputOrdinates = Ordinates.XY };

        await using FileStream stream = CreateStream(path);
        await using StreamWriter writer = new(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        List<string> columns = ["id", GeometryColumn, .. request.Schema.Fields.Select(static f => f.Name)];
        await writer.WriteLineAsync(string.Join(',', columns.Select(Escape))).ConfigureAwait(false);

        long written = 0;
        StringBuilder row = new(256);

        await foreach (GisFeature feature in features.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            row.Clear();
            row.Append(Escape(feature.Id)).Append(',');
            row.Append(Escape(feature.Geometry is null ? string.Empty : wkt.Write(feature.Geometry)));

            foreach (FieldDefinition field in request.Schema.Fields)
            {
                row.Append(',').Append(Escape(feature.GetAttribute(field.Name).ToInvariantString()));
            }

            await writer.WriteLineAsync(row, cancellationToken).ConfigureAwait(false);
            written++;

            if (written % streaming.FlushInterval == 0)
            {
                await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
            }

            ReportProgress(progress, written);
        }

        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

        return written;
    }

    /// <summary>Applies RFC 4180 quoting.</summary>
    /// <remarks>
    /// A value is quoted when it contains a comma, a quote, or any line ending. WKT always contains
    /// commas, so the geometry column is always quoted &#8212; getting this wrong shifts every
    /// column after it and produces a file that parses but is silently wrong.
    /// </remarks>
    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        bool needsQuoting = value.Contains(',', StringComparison.Ordinal)
                            || value.Contains('"', StringComparison.Ordinal)
                            || value.Contains('\n', StringComparison.Ordinal)
                            || value.Contains('\r', StringComparison.Ordinal);

        if (!needsQuoting)
        {
            return value;
        }

        const char Quote = '"';
        const string EscapedQuote = "\"\"";

        StringBuilder escaped = new(value.Length + 8);
        escaped.Append(Quote);
        escaped.Append(value.Replace("\"", EscapedQuote, StringComparison.Ordinal));
        escaped.Append(Quote);

        return escaped.ToString();
    }
}
