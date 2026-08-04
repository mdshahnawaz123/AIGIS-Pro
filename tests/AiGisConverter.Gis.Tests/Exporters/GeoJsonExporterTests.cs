using System.Text.Json;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Exporters.GeoJson;
using AiGisConverter.Gis.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Gis.Tests.Exporters;

public sealed class GeoJsonExporterTests
{
    private static StreamingGeoJsonExporter Exporter() =>
        new(GisOptionsFactory.Monitor(), NullLogger<StreamingGeoJsonExporter>.Instance);

    private static GisFeature[] Single() => [FeatureFactory.Polygon("f1", 0d, 0d)];

    private static async IAsyncEnumerable<GisFeature> Yield(GisFeature[] features)
    {
        foreach (GisFeature feature in features)
        {
            yield return feature;
            await Task.Yield();
        }
    }

    [Fact]
    public async Task WriteAsync_ProducesAValidFeatureCollection()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("parcels.geojson");

        Result<IReadOnlyList<string>> result =
            await Exporter().WriteAsync(FeatureFactory.Request(path), Yield(Single()));

        result.IsSuccess.Should().BeTrue();

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        document.RootElement.GetProperty("type").GetString().Should().Be("FeatureCollection");
        document.RootElement.GetProperty("features").GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task WriteAsync_Wgs84_OmitsTheCrsMemberAsRfc7946Requires()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("wgs84.geojson");

        await Exporter().WriteAsync(FeatureFactory.Request(path, CoordinateSystem.Wgs84), Yield(Single()));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        document.RootElement.TryGetProperty("crs", out _).Should().BeFalse();
    }

    [Fact]
    public async Task WriteAsync_ProjectedData_EmitsTheLegacyCrsMember()
    {
        // RFC 7946 removed crs, but shipping projected eastings labelled as longitudes is worse
        // than a non-conformant hint that QGIS and ArcGIS both honour.
        using TempWorkspace workspace = new();
        string path = workspace.Path("bng.geojson");

        await Exporter().WriteAsync(
            FeatureFactory.Request(path, CoordinateSystem.Create("EPSG", 27700)),
            Yield(Single()));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        document.RootElement.GetProperty("crs")
            .GetProperty("properties").GetProperty("name").GetString()
            .Should().Contain("27700");
    }

    [Fact]
    public async Task WriteAsync_PolygonRingIsClosed()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("ring.geojson");

        await Exporter().WriteAsync(FeatureFactory.Request(path), Yield(Single()));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        JsonElement ring = document.RootElement
            .GetProperty("features")[0]
            .GetProperty("geometry")
            .GetProperty("coordinates")[0];

        ring[0][0].GetDouble().Should().Be(ring[ring.GetArrayLength() - 1][0].GetDouble());
        ring[0][1].GetDouble().Should().Be(ring[ring.GetArrayLength() - 1][1].GetDouble());
    }

    [Fact]
    public async Task WriteAsync_LeadingZeroText_SurvivesAsAString()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("plot.geojson");

        await Exporter().WriteAsync(FeatureFactory.Request(path), Yield(Single()));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        document.RootElement.GetProperty("features")[0]
            .GetProperty("properties").GetProperty("PLOT").GetString()
            .Should().Be("00742");
    }

    [Fact]
    public async Task WriteAsync_Cancellation_RemovesThePartialFile()
    {
        using TempWorkspace workspace = new();
        using CancellationTokenSource cts = new();
        string path = workspace.Path("cancelled.geojson");

        async IAsyncEnumerable<GisFeature> Endless()
        {
            for (int i = 0; ; i++)
            {
                if (i == 50)
                {
                    await cts.CancelAsync();
                }

                yield return FeatureFactory.Polygon($"f{i}", i * 20d, 0d);
            }
        }

        Func<Task> act = async () =>
            await Exporter().WriteAsync(FeatureFactory.Request(path), Endless(), null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();

        // A truncated GeoJSON is not detectably invalid to a reader; it simply has fewer features.
        File.Exists(path).Should().BeFalse();
    }
}
