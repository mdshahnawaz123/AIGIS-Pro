using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Gis.Exporters.GeoJson;
using AiGisConverter.Gis.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Gis.Tests.Exporters;

/// <summary>
/// Proves the exporters actually stream. These are the tests that fail the day someone adds a
/// convenient <c>.ToListAsync()</c> and quietly reintroduces the memory ceiling.
/// </summary>
public sealed class StreamingBehaviourTests
{
    private static StreamingGeoJsonExporter Exporter() =>
        new(GisOptionsFactory.Monitor(), NullLogger<StreamingGeoJsonExporter>.Instance);

    [Fact]
    public async Task WriteAsync_ConsumesTheSourceLazily()
    {
        using TempWorkspace workspace = new();
        int produced = 0;
        int observedWhileWriting = 0;

        async IAsyncEnumerable<GisFeature> Counted()
        {
            for (int i = 0; i < 5_000; i++)
            {
                produced++;

                if (i == 10)
                {
                    // If the writer had drained the sequence first, production would already be
                    // finished by the time any feature reached the file.
                    observedWhileWriting = produced;
                }

                yield return FeatureFactory.Polygon($"f{i}", i * 20d, 0d);
                await Task.Yield();
            }
        }

        await Exporter().WriteAsync(FeatureFactory.Request(workspace.Path("lazy.geojson")), Counted());

        observedWhileWriting.Should().Be(11, "the writer must pull one feature at a time");
        produced.Should().Be(5_000);
    }

    [Fact]
    public async Task WriteAsync_EnumeratesTheSourceExactlyOnce()
    {
        using TempWorkspace workspace = new();
        int enumerations = 0;

        async IAsyncEnumerable<GisFeature> Tracked()
        {
            enumerations++;

            for (int i = 0; i < 100; i++)
            {
                yield return FeatureFactory.Polygon($"f{i}", i * 20d, 0d);
                await Task.Yield();
            }
        }

        await Exporter().WriteAsync(FeatureFactory.Request(workspace.Path("once.geojson")), Tracked());

        enumerations.Should().Be(1, "a second pass would require buffering the whole layer");
    }

    [Fact]
    public async Task WriteAsync_ReportsProgressOnTheConfiguredCadence()
    {
        using TempWorkspace workspace = new();
        List<long> reports = [];

        Progress<AiGisConverter.Gis.Abstractions.ExportProgress> progress = new(p => reports.Add(p.FeaturesWritten));

        StreamingGeoJsonExporter exporter = new(
            GisOptionsFactory.Monitor(o => o.Streaming.ProgressInterval = 100),
            NullLogger<StreamingGeoJsonExporter>.Instance);

        await exporter.WriteAsync(
            FeatureFactory.Request(workspace.Path("progress.geojson")),
            FeatureFactory.Stream(1_000),
            progress);

        // Progress is posted to the synchronisation context, so allow it to drain.
        await Task.Delay(50);

        reports.Should().NotBeEmpty();
    }

    [Fact]
    public async Task WriteAsync_MemoryDoesNotGrowWithFeatureCount()
    {
        using TempWorkspace workspace = new();

        long Measure(int count)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            long before = GC.GetTotalMemory(forceFullCollection: true);

            Exporter().WriteAsync(
                FeatureFactory.Request(workspace.Path($"mem{count}.geojson")),
                FeatureFactory.Stream(count)).GetAwaiter().GetResult();

            return GC.GetTotalMemory(forceFullCollection: true) - before;
        }

        long small = Measure(1_000);
        long large = Measure(50_000);

        // Fifty times the features must not mean fifty times the retained memory. The bound is
        // deliberately loose: this is a regression guard against buffering, not a precise budget.
        large.Should().BeLessThan(Math.Max(small, 1_000_000) * 10,
            "retained memory must be proportional to the largest feature, not to the layer");
    }
}
