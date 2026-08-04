using System.Globalization;
using System.Text.Json;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Plugins.GisExport;

/// <summary>
/// Writes a document as a single RFC 7946 GeoJSON <c>FeatureCollection</c>.
/// </summary>
/// <remarks>
/// <para>
/// Written directly against <see cref="Utf8JsonWriter"/> rather than through a serialisation
/// library. The output is streamed, so a drawing with a million entities does not have to be
/// materialised as an object graph first, and the plugin carries no dependency that could collide
/// with another plugin's.
/// </para>
/// <para>
/// Coordinates are emitted at seven decimal places. RFC 7946 assumes WGS 84, where seven places is
/// roughly a centimetre; more digits would imply a precision the source survey does not have.
/// </para>
/// </remarks>
internal sealed class GeoJsonExporter : IFeatureExporter
{
    private const int CoordinatePrecision = 7;

    /// <inheritdoc />
    public string FormatKey => "geojson";

    /// <inheritdoc />
    public string DisplayName => "GeoJSON";

    /// <inheritdoc />
    public string FileExtension => ".geojson";

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> ExportAsync(
        SourceDocument document,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string path = Path.HasExtension(outputPath) ? outputPath : outputPath + FileExtension;

        try
        {
            string? directory = Path.GetDirectoryName(path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using FileStream stream = File.Create(path);
            await using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false });

            writer.WriteStartObject();
            writer.WriteString("type", "FeatureCollection");
            WriteCrs(writer, document.DeclaredCrs);
            writer.WriteStartArray("features");

            foreach (SourceLayer layer in document.Layers)
            {
                foreach (SourceElement element in layer.Elements)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    WriteFeature(writer, layer, element);
                }
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success<IReadOnlyList<string>>([path]);
        }
        catch (IOException ex)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error("Export.IoFailure", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error("Export.AccessDenied", ex.Message));
        }
    }

    /// <summary>
    /// Writes the legacy named-CRS member when the document declares a system other than WGS 84.
    /// </summary>
    /// <remarks>
    /// RFC 7946 removed the <c>crs</c> member and mandates WGS 84. Emitting it anyway when the data
    /// is in a projected system is the lesser evil: QGIS and ArcGIS both honour it, and silently
    /// shipping British National Grid eastings labelled as longitudes is far worse.
    /// </remarks>
    private static void WriteCrs(Utf8JsonWriter writer, string? declaredCrs)
    {
        if (string.IsNullOrWhiteSpace(declaredCrs) ||
            declaredCrs.Equals("EPSG:4326", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        writer.WriteStartObject("crs");
        writer.WriteString("type", "name");
        writer.WriteStartObject("properties");
        writer.WriteString("name", declaredCrs);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WriteFeature(Utf8JsonWriter writer, SourceLayer layer, SourceElement element)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "Feature");
        writer.WriteString("id", element.Id);

        writer.WriteStartObject("properties");
        writer.WriteString("layer", layer.Name);
        writer.WriteString("geometryKind", element.GeometryKind.ToString());

        if (element.NativeType is not null)
        {
            writer.WriteString("nativeType", element.NativeType);
        }

        if (element.Text is not null)
        {
            writer.WriteString("text", element.Text);
        }

        foreach (KeyValuePair<string, object?> attribute in element.Attributes)
        {
            WriteAttribute(writer, attribute.Key, attribute.Value);
        }

        writer.WriteEndObject();

        writer.WritePropertyName("geometry");
        WriteGeometry(writer, element.Geometry);

        writer.WriteEndObject();
    }

    private static void WriteAttribute(Utf8JsonWriter writer, string name, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNull(name);
                break;
            case string text:
                writer.WriteString(name, text);
                break;
            case bool flag:
                writer.WriteBoolean(name, flag);
                break;
            case int number:
                writer.WriteNumber(name, number);
                break;
            case long number:
                writer.WriteNumber(name, number);
                break;
            case double number:
                writer.WriteNumber(name, number);
                break;
            case decimal number:
                writer.WriteNumber(name, number);
                break;
            case DateTime timestamp:
                writer.WriteString(name, timestamp.ToString("O", CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteString(name, Convert.ToString(value, CultureInfo.InvariantCulture));
                break;
        }
    }

    private static void WriteGeometry(Utf8JsonWriter writer, Geometry? geometry)
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
                WritePolygonRings(writer, polygon);
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

                foreach (Geometry part in multiLine.Geometries)
                {
                    WriteCoordinates(writer, part.Coordinates);
                }

                writer.WriteEndArray();
                break;

            case MultiPolygon multiPolygon:
                writer.WriteString("type", "MultiPolygon");
                writer.WritePropertyName("coordinates");
                writer.WriteStartArray();

                foreach (Geometry part in multiPolygon.Geometries)
                {
                    WritePolygonRings(writer, (Polygon)part);
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

    private static void WritePolygonRings(Utf8JsonWriter writer, Polygon polygon)
    {
        writer.WriteStartArray();
        WriteCoordinates(writer, polygon.ExteriorRing.Coordinates);

        foreach (LineString ring in polygon.InteriorRings)
        {
            WriteCoordinates(writer, ring.Coordinates);
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
        writer.WriteNumberValue(Math.Round(coordinate.X, CoordinatePrecision));
        writer.WriteNumberValue(Math.Round(coordinate.Y, CoordinatePrecision));

        if (!double.IsNaN(coordinate.Z))
        {
            writer.WriteNumberValue(Math.Round(coordinate.Z, CoordinatePrecision));
        }

        writer.WriteEndArray();
    }
}
