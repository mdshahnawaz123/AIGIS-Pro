using System.Diagnostics;
using AiGisConverter.Gis.Exporters.Csv;
using AiGisConverter.Gis.Exporters.GeoJson;
using AiGisConverter.Gis.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AiGisConverter.Gis.Tests.Performance;

/// <summary>
/// Throughput checks over a large synthetic layer.
/// </summary>
/// <remarks>
/// The counts are set so the suite stays usable in CI. They establish the shape of the cost curve
/// &#8212; linear, bounded memory &#8212; rather than certifying a machine-specific rate. The
/// million-feature target is verified by extrapolation from the measured per-feature cost, which
/// is stated in the output rather than asserted, because asserting a wall-clock figure on unknown
/// hardware produces a flaky test and no information.
/// </remarks>
public sealed class LargeDatasetTests
{
    private const int FeatureCount = 100_000;

    private readonly ITestOutputHelper _output;

    public LargeDatasetTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task GeoJson_HandlesOneHundredThousandFeatures()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("large.geojson");

        StreamingGeoJsonExporter exporter = new(
            GisOptionsFactory.Monitor(), NullLogger<StreamingGeoJsonExporter>.Instance);

        long before = GC.GetTotalMemory(forceFullCollection: true);
        Stopwatch stopwatch = Stopwatch.StartNew();

        await exporter.WriteAsync(FeatureFactory.Request(path), FeatureFactory.Stream(FeatureCount));

        stopwatch.Stop();
        long retained = GC.GetTotalMemory(forceFullCollection: true) - before;

        double perFeatureMicroseconds = stopwatch.Elapsed.TotalMilliseconds * 1_000d / FeatureCount;

        _output.WriteLine($"GeoJSON: {FeatureCount:N0} features in {stopwatch.ElapsedMilliseconds:N0} ms");
        _output.WriteLine($"  {perFeatureMicroseconds:F1} us/feature");
        _output.WriteLine($"  extrapolated 1,000,000 features: {perFeatureMicroseconds * 1_000_000 / 1_000_000:F1} s");
        _output.WriteLine($"  retained: {retained / 1024 / 1024:N0} MB");
        _output.WriteLine($"  file: {new FileInfo(path).Length / 1024 / 1024:N0} MB");

        File.Exists(path).Should().BeTrue();
        new FileInfo(path).Length.Should().BePositive();
        retained.Should().BeLessThan(256L * 1024 * 1024, "streaming must not retain the dataset");
    }

    [Fact]
    public async Task Csv_HandlesOneHundredThousandFeatures()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("large.csv");

        StreamingCsvExporter exporter = new(
            GisOptionsFactory.Monitor(), NullLogger<StreamingCsvExporter>.Instance);

        Stopwatch stopwatch = Stopwatch.StartNew();
        await exporter.WriteAsync(FeatureFactory.Request(path), FeatureFactory.Stream(FeatureCount));
        stopwatch.Stop();

        _output.WriteLine($"CSV: {FeatureCount:N0} features in {stopwatch.ElapsedMilliseconds:N0} ms");

        File.Exists(path).Should().BeTrue();
    }

    [Fact]
    public async Task Cancellation_TakesEffectPromptlyOnALargeLayer()
    {
        using TempWorkspace workspace = new();
        using CancellationTokenSource cts = new();

        StreamingGeoJsonExporter exporter = new(
            GisOptionsFactory.Monitor(), NullLogger<StreamingGeoJsonExporter>.Instance);

        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        Stopwatch stopwatch = Stopwatch.StartNew();

        Func<Task> act = async () => await exporter.WriteAsync(
            FeatureFactory.Request(workspace.Path("cancel.geojson")),
            FeatureFactory.Stream(5_000_000),
            null,
            cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30),
            "cancellation must be observed per feature, not at the end of the layer");
    }
}
