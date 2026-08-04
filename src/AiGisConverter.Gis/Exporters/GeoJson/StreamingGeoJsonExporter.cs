using System.Globalization;
using System.Text.Json;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Exporters.GeoJson;

/// <summary>
/// Writes an RFC 7946 <c>FeatureCollection</c>, streaming.
/// </summary>
/// <remarks>
/// <para>
/// Written directly against <see cref="Utf8JsonWriter"/> rather than through a serialisation
/// library, so memory stays flat regardless of feature count: nothing larger than one feature is
/// ever materialised. A million features costs the same working set as a hundred.
/// </para>
/// <para>
/// RFC 7946 removed the <c>crs</c> member and mandates WGS 84. This writer emits the legacy member
/// anyway when the data is in a projected system, because silently shipping British National Grid
/// eastings labelled as longitudes is far worse than a non-conformant hint that QGIS and ArcGIS
/// both honour.
/// </para>
/// </remarks>
public sealed class StreamingGeoJsonExporter : IStreamingExporter
{
    private readonly IOptionsMonitor<GisOptions> _options;
    private readonly ILogger<StreamingGeoJsonExporter> _logger;

    /// <summary>Initializes a new instance of the <see cref="StreamingGeoJsonExporter"/> class.</summary>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the exporter.</param>
    public StreamingGeoJsonExporter(IOptionsMonitor<GisOptions> options, ILogger<StreamingGeoJsonExporter> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string FormatKey => "geojson";

    /// <inheritdoc />
    public ExportFormat Format => ExportFormat.GeoJson;

    /// <inheritdoc />
    public string FileExtension => ".geojson";

    /// <inheritdoc />
    public bool SupportsMultipleLayers => false;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> WriteAsync(
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(features);

        StreamingOptions streaming = _options.CurrentValue.Streaming;
        string path = Path.HasExtension(request.OutputPath) ? request.OutputPath : request.OutputPath + FileExtension;

        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = new(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                streaming.BufferSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);

            await using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false, SkipValidation = false });

            writer.WriteStartObject();
            writer.WriteString("type", "FeatureCollection");
            WriteCrs(writer, request.CoordinateSystem);
            writer.WriteStartArray("features");

            long written = 0;

            await foreach (GisFeature feature in features.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                cancellationToken.ThrowIfCancellationRequested();

                WriteFeature(writer, feature);
                written++;

                if (written % streaming.FlushInterval == 0)
                {
                    await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
                }

                if (written % streaming.ProgressInterval == 0)
                {
                    progress?.Report(new ExportProgress(written, $"Written {written:N0} features..."));
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation(
                "Wrote {FeatureCount} features to {Path} in {Crs}.",
                written,
                path,
                request.CoordinateSystem.Identifier);

            progress?.Report(new ExportProgress(written, $"Wrote {written:N0} features."));

            return Result.Success<IReadOnlyList<string>>([path]);
        }
        catch (OperationCanceledException)
        {
            TryDeletePartialOutput(path);
            throw;
        }
        catch (IOException ex)
        {
            TryDeletePartialOutput(path);
            return Result.Failure<IReadOnlyList<string>>(new Error("Export.IoFailure", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error("Export.AccessDenied", ex.Message));
        }
    }

    /// <summary>
    /// Removes a half-written file.
    /// </summary>
    /// <remarks>
    /// A truncated GeoJSON is not detectably invalid to a casual reader &#8212; it simply contains
    /// fewer features than the drawing did. Leaving one behind after a cancellation invites someone
    /// to use it.
    /// </remarks>
    private void TryDeletePartialOutput(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Could not remove the partial output at {Path}.", path);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Could not remove the partial output at {Path}.", path);
        }
    }

    private static void WriteCrs(Utf8JsonWriter writer, CoordinateSystem coordinateSystem)
    {
        if (coordinateSystem == CoordinateSystem.Wgs84)
        {
            return;
        }

        writer.WriteStartObject("crs");
        writer.WriteString("type", "name");
        writer.WriteStartObject("properties");
        writer.WriteString("name", $"urn:ogc:def:crs:{coordinateSystem.Authority}::{coordinateSystem.Code}");
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteFeature(Utf8JsonWriter writer, GisFeature feature)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");
        writer.WriteString("id", feature.Id);

        writer.WriteStartObject("properties");

        foreach (KeyValuePair<string, AttributeValue> attribute in feature.Attributes)
        {
            WriteAttribute(writer, attribute.Key, attribute.Value);
        }

        writer.WriteEndObject();

        writer.WritePropertyName("geometry");
        WriteGeometry(writer, feature.Geometry);

        writer.WriteEndObject();
    }

    private static void WriteAttribute(Utf8JsonWriter writer, string name, AttributeValue value)
    {
        if (value.IsNull)
        {
            writer.WriteNull(name);
            return;
        }

        switch (value.RawValue)
        {
            case bool flag:
                writer.WriteBoolean(name, flag);
                break;
            case int number:
                writer.WriteNumber(name, number);
                break;
            case long number:
                writer.WriteNumber(name, number);
                break;
            case double number when double.IsFinite(number):
                writer.WriteNumber(name, number);
                break;
            default:
                // Text, timestamps, and non-finite numbers, which JSON cannot represent.
                writer.WriteString(name, value.ToInvariantString());
                break;
        }
    }

    private static void WriteGeometry(Utf8JsonWriter writer, NtsGeometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartObject();

        switch (geometry)
        {
            case Point point:
                writer.WriteString("type", "Point");
                writer.WritePropertyName("coordinates");
                WriteCoordinate(writer, point.Coordinate);
                break;

            case LineString line:
                writer.WriteString("type", "LineString");
                writer.WritePropertyName("coordinates");
                WriteCoordinates(writer, line.Coordinates);
                break;

            case Polygon polygon:
                writer.WriteString("type", "Polygon");
                writer.WritePropertyName("coordinates");
                WriteRings(writer, polygon);
                break;

            case MultiPoint multiPoint:
                writer.WriteString("type", "MultiPoint");
                writer.WritePropertyName("coordinates");
                WriteCoordinates(writer, multiPoint.Coordinates);
                break;

            case MultiLineString multiLine:
                writer.WriteString("type", "MultiLineString");
                writer.WritePropertyName("coordinates");
                writer.WriteStartArray();

                foreach (NtsGeometry part in multiLine.Geometries)
                {
                    WriteCoordinates(writer, part.Coordinates);
                }

                writer.WriteEndArray();
                break;

            case MultiPolygon multiPolygon:
                writer.WriteString("type", "MultiPolygon");
                writer.WritePropertyName("coordinates");
                writer.WriteStartArray();

                foreach (NtsGeometry part in multiPolygon.Geometries)
                {
                    WriteRings(writer, (Polygon)part);
                }

                writer.WriteEndArray();
                break;

            default:
                writer.WriteString("type", "GeometryCollection");
                writer.WriteStartArray("geometries");

                for (int i = 0; i < geometry.NumGeometries; i++)
                {
                    WriteGeometry(writer, geometry.GetGeometryN(i));
                }

                writer.WriteEndArray();
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteRings(Utf8JsonWriter writer, Polygon polygon)
    {
        writer.WriteStartArray();
        WriteCoordinates(writer, polygon.ExteriorRing.Coordinates);

        foreach (LineString hole in polygon.InteriorRings)
        {
            WriteCoordinates(writer, hole.Coordinates);
        }

        writer.WriteEndArray();
    }

    private static void WriteCoordinates(Utf8JsonWriter writer, Coordinate[] coordinates)
    {
        writer.WriteStartArray();

        foreach (Coordinate coordinate in coordinates)
        {
            WriteCoordinate(writer, coordinate);
        }

        writer.WriteEndArray();
    }

    private static void WriteCoordinate(Utf8JsonWriter writer, Coordinate coordinate)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(coordinate.X);
        writer.WriteNumberValue(coordinate.Y);

        if (!double.IsNaN(coordinate.Z))
        {
            writer.WriteNumberValue(coordinate.Z);
        }

        writer.WriteEndArray();
    }
}
