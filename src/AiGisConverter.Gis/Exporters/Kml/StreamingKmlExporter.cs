using System.Globalization;
using System.Text;
using System.Xml;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Exporters.Kml;

/// <summary>
/// Writes OGC KML 2.2, streaming, one Placemark per feature.
/// </summary>
/// <remarks>
/// <para>
/// KML is defined only over WGS 84 geographic coordinates. Data in any other system is written
/// anyway &#8212; refusing would be less useful than producing a file the operator can see is in
/// the wrong place &#8212; but a critical finding is recorded, because a KML of British National
/// Grid eastings opens in Google Earth somewhere off the coast of Africa with no error at all.
/// </para>
/// <para>
/// Coordinates are emitted at seven decimal places, roughly a centimetre in WGS 84. More digits
/// would imply a precision the source survey does not have and would inflate the file for nothing.
/// </para>
/// </remarks>
public sealed class StreamingKmlExporter : StreamingExporterBase
{
    private const int CoordinatePrecision = 7;
    private const string KmlNamespace = "http://www.opengis.net/kml/2.2";

    /// <summary>Initializes a new instance of the <see cref="StreamingKmlExporter"/> class.</summary>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the exporter.</param>
    public StreamingKmlExporter(IOptionsMonitor<GisOptions> options, ILogger<StreamingKmlExporter> logger)
        : base(options, logger)
    {
    }

    /// <inheritdoc />
    public override string FormatKey => "kml";

    /// <inheritdoc />
    public override ExportFormat Format => ExportFormat.Kml;

    /// <inheritdoc />
    public override string FileExtension => ".kml";

    /// <inheritdoc />
    protected override async Task<long> WriteCoreAsync(
        string path,
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken)
    {
        WarnIfNotWgs84(request);

        StreamingOptions streaming = Options.CurrentValue.Streaming;

        XmlWriterSettings settings = new()
        {
            Indent = false,
            Encoding = new UTF8Encoding(false),
            Async = true,
            CloseOutput = false,
        };

        await using FileStream stream = CreateStream(path);
        await using XmlWriter writer = XmlWriter.Create(stream, settings);

        await writer.WriteStartDocumentAsync().ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "kml", KmlNamespace).ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "Document", KmlNamespace).ConfigureAwait(false);
        await writer.WriteElementStringAsync(null, "name", KmlNamespace, request.FeatureClass.Name)
            .ConfigureAwait(false);

        long written = 0;

        await foreach (GisFeature feature in features.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            await WritePlacemarkAsync(writer, feature, request).ConfigureAwait(false);
            written++;

            if (written % streaming.FlushInterval == 0)
            {
                await writer.FlushAsync().ConfigureAwait(false);
            }

            ReportProgress(progress, written);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false);
        await writer.WriteEndDocumentAsync().ConfigureAwait(false);
        await writer.FlushAsync().ConfigureAwait(false);

        return written;
    }

    private void WarnIfNotWgs84(ExportRequest request)
    {
        if (request.CoordinateSystem == CoordinateSystem.Wgs84)
        {
            return;
        }

        request.Context.Record(ValidationIssue.Create(
            IssueSeverity.Critical,
            IssueCategory.Crs,
            "Export.KmlRequiresWgs84",
            $"KML is defined only over WGS 84 but the data is in {request.CoordinateSystem.Identifier}. " +
            "The file will open without error in the wrong place. Reproject to EPSG:4326 before delivering it."));

        Logger.LogError(
            "KML export requested for {Crs}, which KML cannot represent.",
            request.CoordinateSystem.Identifier);
    }

    private static async Task WritePlacemarkAsync(XmlWriter writer, GisFeature feature, ExportRequest request)
    {
        await writer.WriteStartElementAsync(null, "Placemark", KmlNamespace).ConfigureAwait(false);
        await writer.WriteAttributeStringAsync(null, "id", null, feature.Id).ConfigureAwait(false);

        if (request.Schema.Fields.Count > 0)
        {
            await writer.WriteStartElementAsync(null, "ExtendedData", KmlNamespace).ConfigureAwait(false);

            foreach (FieldDefinition field in request.Schema.Fields)
            {
                await writer.WriteStartElementAsync(null, "Data", KmlNamespace).ConfigureAwait(false);
                await writer.WriteAttributeStringAsync(null, "name", null, field.Name).ConfigureAwait(false);
                await writer.WriteElementStringAsync(
                    null,
                    "value",
                    KmlNamespace,
                    feature.GetAttribute(field.Name).ToInvariantString()).ConfigureAwait(false);
                await writer.WriteEndElementAsync().ConfigureAwait(false);
            }

            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        if (feature.Geometry is not null && !feature.Geometry.IsEmpty)
        {
            await WriteGeometryAsync(writer, feature.Geometry).ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task WriteGeometryAsync(XmlWriter writer, NtsGeometry geometry)
    {
        switch (geometry)
        {
            case Point point:
                await writer.WriteStartElementAsync(null, "Point", KmlNamespace).ConfigureAwait(false);
                await WriteCoordinatesAsync(writer, [point.Coordinate]).ConfigureAwait(false);
                await writer.WriteEndElementAsync().ConfigureAwait(false);
                break;

            case LineString line:
                await writer.WriteStartElementAsync(null, "LineString", KmlNamespace).ConfigureAwait(false);
                await WriteCoordinatesAsync(writer, line.Coordinates).ConfigureAwait(false);
                await writer.WriteEndElementAsync().ConfigureAwait(false);
                break;

            case Polygon polygon:
                await WritePolygonAsync(writer, polygon).ConfigureAwait(false);
                break;

            default:
                // KML has no multi-geometry primitive of its own; MultiGeometry is the container
                // for every multi-part and heterogeneous case alike.
                await writer.WriteStartElementAsync(null, "MultiGeometry", KmlNamespace).ConfigureAwait(false);

                for (int i = 0; i < geometry.NumGeometries; i++)
                {
                    await WriteGeometryAsync(writer, geometry.GetGeometryN(i)).ConfigureAwait(false);
                }

                await writer.WriteEndElementAsync().ConfigureAwait(false);
                break;
        }
    }

    private static async Task WritePolygonAsync(XmlWriter writer, Polygon polygon)
    {
        await writer.WriteStartElementAsync(null, "Polygon", KmlNamespace).ConfigureAwait(false);

        await writer.WriteStartElementAsync(null, "outerBoundaryIs", KmlNamespace).ConfigureAwait(false);
        await writer.WriteStartElementAsync(null, "LinearRing", KmlNamespace).ConfigureAwait(false);
        await WriteCoordinatesAsync(writer, polygon.ExteriorRing.Coordinates).ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false);
        await writer.WriteEndElementAsync().ConfigureAwait(false);

        foreach (LineString hole in polygon.InteriorRings)
        {
            await writer.WriteStartElementAsync(null, "innerBoundaryIs", KmlNamespace).ConfigureAwait(false);
            await writer.WriteStartElementAsync(null, "LinearRing", KmlNamespace).ConfigureAwait(false);
            await WriteCoordinatesAsync(writer, hole.Coordinates).ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
            await writer.WriteEndElementAsync().ConfigureAwait(false);
        }

        await writer.WriteEndElementAsync().ConfigureAwait(false);
    }

    private static async Task WriteCoordinatesAsync(XmlWriter writer, Coordinate[] coordinates)
    {
        StringBuilder builder = new(coordinates.Length * 24);

        foreach (Coordinate coordinate in coordinates)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(Math.Round(coordinate.X, CoordinatePrecision).ToString(CultureInfo.InvariantCulture))
                   .Append(',')
                   .Append(Math.Round(coordinate.Y, CoordinatePrecision).ToString(CultureInfo.InvariantCulture));

            if (!double.IsNaN(coordinate.Z))
            {
                builder.Append(',')
                       .Append(Math.Round(coordinate.Z, CoordinatePrecision).ToString(CultureInfo.InvariantCulture));
            }
        }

        await writer.WriteElementStringAsync(null, "coordinates", KmlNamespace, builder.ToString())
            .ConfigureAwait(false);
    }
}
