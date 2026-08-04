using System.Globalization;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Plugins.CustomExtensions;

/// <summary>
/// Reads a delimited survey point file: a header row, then one point per line.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately small and complete. It is here so the template can be run against a real file
/// rather than only read, and so the shape of a correct reader is visible: claim the file in
/// <see cref="CanRead"/>, stream it, report progress, and return a failed
/// <see cref="Result"/> instead of throwing when the input is not what was promised.
/// </para>
/// <para>
/// Columns named <c>x</c>/<c>easting</c>, <c>y</c>/<c>northing</c> and <c>z</c>/<c>elevation</c>
/// become geometry; a <c>layer</c> or <c>code</c> column groups the points; every other column
/// becomes an attribute.
/// </para>
/// </remarks>
internal sealed class DelimitedPointReader : IDataSourceReader
{
    private static readonly string[] XNames = ["x", "easting", "east", "e", "lon", "longitude"];
    private static readonly string[] YNames = ["y", "northing", "north", "n", "lat", "latitude"];
    private static readonly string[] ZNames = ["z", "elevation", "elev", "height", "level"];
    private static readonly string[] LayerNames = ["layer", "code", "class", "feature"];

    private static readonly GeometryFactory GeometryFactory = new();

    /// <inheritdoc />
    public string FormatKey => "points-csv";

    /// <inheritdoc />
    public string DisplayName => "Delimited survey points";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = [".csv", ".txt", ".pnt"];

    /// <inheritdoc />
    public bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase)
               && File.Exists(reference.Location);
    }

    /// <inheritdoc />
    public async Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        SourceDocument document = new(reference, FormatKey);
        Dictionary<string, SourceLayer> layers = new(StringComparer.OrdinalIgnoreCase);

        using StreamReader reader = new(reference.Location);

        string? headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

        if (headerLine is null)
        {
            return Result.Failure<SourceDocument>(new Error("Reader.EmptyFile", "The file is empty."));
        }

        char delimiter = DetectDelimiter(headerLine);
        string[] headers = headerLine.Split(delimiter);

        int xIndex = IndexOfAny(headers, XNames);
        int yIndex = IndexOfAny(headers, YNames);

        if (xIndex < 0 || yIndex < 0)
        {
            return Result.Failure<SourceDocument>(new Error(
                "Reader.NoCoordinateColumns",
                "No coordinate columns were found. Expected a header naming an X/easting and a Y/northing column."));
        }

        int zIndex = IndexOfAny(headers, ZNames);
        int layerIndex = IndexOfAny(headers, LayerNames);
        int lineNumber = 1;

        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lineNumber++;

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            string[] fields = line.Split(delimiter);

            if (fields.Length <= Math.Max(xIndex, yIndex) ||
                !TryParse(fields[xIndex], out double x) ||
                !TryParse(fields[yIndex], out double y))
            {
                document.AddWarning($"Line {lineNumber} was skipped: unreadable coordinates.");
                continue;
            }

            double z = zIndex >= 0 && zIndex < fields.Length && TryParse(fields[zIndex], out double parsedZ)
                ? parsedZ
                : double.NaN;

            string layerName = layerIndex >= 0 && layerIndex < fields.Length && !string.IsNullOrWhiteSpace(fields[layerIndex])
                ? fields[layerIndex].Trim()
                : "Points";

            if (!layers.TryGetValue(layerName, out SourceLayer? layer))
            {
                layer = new SourceLayer(layerName);
                layers[layerName] = layer;
                document.AddLayer(layer);
            }

            SourceElement element = new(
                lineNumber.ToString(CultureInfo.InvariantCulture),
                GeometryKind.Point)
            {
                Geometry = GeometryFactory.CreatePoint(new CoordinateZ(x, y, z)),
                NativeType = "POINT",
            };

            for (int i = 0; i < headers.Length && i < fields.Length; i++)
            {
                if (i == xIndex || i == yIndex || i == zIndex || i == layerIndex)
                {
                    continue;
                }

                element.SetAttribute(headers[i].Trim(), fields[i].Trim());
            }

            layer.AddElement(element);

            if (lineNumber % 5000 == 0)
            {
                progress?.Report(new ReadProgress(null, $"Read {lineNumber:N0} points..."));
            }
        }

        progress?.Report(new ReadProgress(1d, $"Read {document.CountElements():N0} points."));

        return Result.Success(document);
    }

    /// <summary>Picks the delimiter that splits the header into the most columns.</summary>
    private static char DetectDelimiter(string headerLine)
    {
        char best = ',';
        int bestCount = 0;

        foreach (char candidate in new[] { ',', ';', '\t', '|' })
        {
            int count = headerLine.Count(c => c == candidate);

            if (count > bestCount)
            {
                bestCount = count;
                best = candidate;
            }
        }

        return best;
    }

    private static int IndexOfAny(string[] headers, string[] names)
    {
        for (int i = 0; i < headers.Length; i++)
        {
            string header = headers[i].Trim();

            if (names.Contains(header, StringComparer.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static bool TryParse(string value, out double result) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);
}
