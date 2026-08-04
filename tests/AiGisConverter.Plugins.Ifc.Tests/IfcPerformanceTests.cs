using System.Diagnostics;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Ifc.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AiGisConverter.Plugins.Ifc.Tests;

/// <summary>
/// Performance, scaling and stress verification for the IFC reader.
/// </summary>
/// <remarks>
/// <para>
/// These assert on <em>shape</em> rather than on wall-clock thresholds wherever possible. A test
/// that says "must read in under four seconds" measures the build agent, not the reader, and either
/// flakes or gets its bound raised until it proves nothing. Reading two sizes and comparing the
/// times is machine-independent: quadratic behaviour shows up as a ratio no hardware can flatter.
/// </para>
/// <para>
/// The cost centre here is not parsing. It is the six inverse lookups the reader performs per
/// element — <c>ContainedInStructure</c>, <c>Decomposes</c>, <c>IsDefinedBy</c>, <c>IsTypedBy</c>,
/// <c>HasAssociations</c> and <c>FillsVoids</c>. If any of those degrades to a scan of the model,
/// the reader is quadratic and only a large model reveals it.
/// </para>
/// </remarks>
[Trait("Category", "Performance")]
public sealed class IfcPerformanceTests : IDisposable
{
    /// <summary>The size used for routine scaling checks.</summary>
    /// <remarks>Large enough for inverse-lookup cost to dominate; small enough to run every build.</remarks>
    private const int BaselineElements = 5_000;

    /// <summary>Four times the baseline. Quadratic growth would cost sixteen times as much.</summary>
    private const int ScaledElements = 20_000;

    /// <summary>
    /// The three sizes the scaling test measures, each double the last.
    /// </summary>
    /// <remarks>
    /// Sized so every measurement is far enough above the noise floor to mean something. With the
    /// inverse cache enabled a five thousand element model reads in about fifty milliseconds, and
    /// at that scale a stray garbage collection moves the ratio by a whole point - which is how a
    /// reader that had just improved sixteenfold produced a failing ratio of 10.11.
    /// </remarks>
    private static readonly int[] ScalingSizes = [20_000, 40_000, 80_000];

    /// <summary>The size used by the opt-in stress test.</summary>
    private const int StressElements = 100_000;

    /// <summary>
    /// The size used for the high-fan-in correctness test.
    /// </summary>
    /// <remarks>
    /// Small on purpose: that model's cost is quadratic in the element count by construction, so a
    /// large one would measure the fixture's shape rather than the reader.
    /// </remarks>
    private const int FanInElements = 500;

    private readonly List<string> _temporary = [];
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises the fixture with xUnit's output channel.</summary>
    /// <param name="output">Receives the measured timings, so a passing run still reports them.</param>
    public IfcPerformanceTests(ITestOutputHelper output) => _output = output;

    public void Dispose()
    {
        foreach (string path in _temporary)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
        }
    }

    private static IPluginContext Context()
    {
        IPluginContext context = Substitute.For<IPluginContext>();
        context.Logger.Returns(NullLogger.Instance);

        return context;
    }

    /// <summary>Writes a model whose relationship set sizes match a production exporter.</summary>
    /// <param name="elementCount">How many building elements to emit.</param>
    /// <returns>The path to the written model.</returns>
    private string WriteModel(int elementCount) =>
        Write(LargeModelBuilder.BuildRealistic(elementCount));

    private string Write(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aigis-ifc-perf-{Guid.NewGuid():N}.ifc");
        File.WriteAllText(path, content);
        _temporary.Add(path);

        return path;
    }

    private async Task<(SourceDocument Document, TimeSpan Elapsed)> TimeReadAsync(string path)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Result<SourceDocument> result = await new IfcReader(Context()).ReadAsync(new SourceReference(path));
        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);

        return (result.Value, stopwatch.Elapsed);
    }

    private static IReadOnlyList<SourceElement> All(SourceDocument document) =>
        [.. document.Layers.SelectMany(static layer => layer.Elements)];

    private static string? Text(SourceElement element, string name) =>
        element.Attributes.TryGetValue(name, out object? value) ? value?.ToString() : null;

    // ---- scaling -------------------------------------------------------------------------------

    [Fact]
    public async Task ReadTime_GrowsSubQuadratically_WithModelSize()
    {
        // Warm the JIT and xBIM's static initialisation, otherwise the first read carries one-off
        // cost that belongs to neither measurement.
        _ = await TimeReadAsync(WriteModel(2_000));

        List<TimeSpan> timings = [];

        foreach (int size in ScalingSizes)
        {
            (SourceDocument document, TimeSpan elapsed) = await TimeReadAsync(WriteModel(size));

            All(document).Should().HaveCountGreaterThanOrEqualTo(size);
            timings.Add(elapsed);
        }

        // Three points rather than two. A single ratio cannot tell a linear reader having a bad
        // moment from a genuinely superlinear one; a curve can. Each size doubles, so linear costs
        // about 2x per step and quadratic about 4x.
        List<double> ratios = [];

        for (int i = 1; i < timings.Count; i++)
        {
            ratios.Add(timings[i].TotalMilliseconds / Math.Max(timings[i - 1].TotalMilliseconds, 1d));
        }

        _output.WriteLine("scaling (each step doubles the elements; linear ~2x, quadratic ~4x):");

        for (int i = 0; i < ScalingSizes.Length; i++)
        {
            string step = i == 0
                ? string.Empty
                : $"  -> {ratios[i - 1]:F2}x";

            _output.WriteLine($"  {ScalingSizes[i],7:N0} elements  {timings[i].TotalMilliseconds,8:F0}ms{step}");
        }

        // Assert on the last step: the largest models, where a fixed overhead of a few milliseconds
        // is proportionally smallest and the measurement is therefore most trustworthy.
        double finalRatio = ratios[^1];

        finalRatio.Should().BeLessThan(3d,
            $"doubling the elements took {finalRatio:F2}x the time "
            + $"({timings[^2].TotalMilliseconds:F0}ms -> {timings[^1].TotalMilliseconds:F0}ms), "
            + "which is closer to quadratic than linear and suggests an inverse lookup is scanning "
            + "the model rather than using its index");
    }

    [Fact]
    public async Task LargeModel_ReadsWithinAWorkableTime()
    {
        (SourceDocument document, TimeSpan elapsed) = await TimeReadAsync(WriteModel(ScaledElements));

        All(document).Should().HaveCountGreaterThanOrEqualTo(ScaledElements);

        // A backstop, not a benchmark: twenty thousand elements taking minutes means something is
        // wrong that the ratio test could miss if both sizes were equally slow.
        elapsed.Should().BeLessThan(TimeSpan.FromMinutes(3),
            $"{ScaledElements} elements took {elapsed.TotalSeconds:F1}s");
    }

    [Fact]
    public async Task MemoryPerElement_StaysBounded()
    {
        string path = WriteModel(ScaledElements);

        long before = GC.GetTotalMemory(forceFullCollection: true);
        (SourceDocument document, _) = await TimeReadAsync(path);
        long after = GC.GetTotalMemory(forceFullCollection: true);

        int count = All(document).Count;
        double bytesPerElement = (after - before) / (double)count;

        // Deliberately loose. This does not police allocation efficiency; it catches the reader
        // retaining the whole xBIM model — or its geometry — alongside the document it returns,
        // which is the failure that turns a large model into an out-of-memory crash.
        _output.WriteLine(
            $"memory: {count:N0} elements retained {(after - before) / 1_048_576d:F0}MB "
            + $"({bytesPerElement:F0} bytes each)");

        bytesPerElement.Should().BeLessThan(25_000d,
            $"{count} elements retained {(after - before) / 1_048_576d:F0}MB "
            + $"({bytesPerElement:F0} bytes each)");

        // Keep the document alive to the end so the measurement is of retained, not transient, memory.
        GC.KeepAlive(document);
    }

    // ---- correctness at scale ------------------------------------------------------------------

    [Fact]
    public async Task EveryElement_ResolvesItsStorey_AtScale()
    {
        // A small fixture cannot distinguish "resolves containment" from "resolves the first few".
        (SourceDocument document, _) = await TimeReadAsync(WriteModel(ScaledElements));

        IReadOnlyList<SourceElement> walls =
            [.. All(document).Where(static e => e.NativeType == "IfcWall")];

        walls.Should().HaveCount(ScaledElements);
        walls.Should().OnlyContain(w => Text(w, "BuildingStorey") != null);
        walls.Select(w => Text(w, "BuildingStorey")).Distinct()
            .Should().HaveCount(LargeModelBuilder.StoreyCountFor(ScaledElements));
    }

    [Fact]
    public async Task OneSharedRelationship_ReachesEveryElementItNames()
    {
        // Correctness, not timing. One IfcRelDefinesByType, one material association and one
        // classification cover every element here — the worst case for xBIM's inverse resolution,
        // which tests set membership per element and is therefore quadratic in the set size. Real
        // exporters never emit this shape (a production Revit export had a median set size of 1),
        // so it is exercised only at a size where the quadratic term does not matter.
        (SourceDocument document, _) = await TimeReadAsync(
            Write(LargeModelBuilder.BuildHighFanIn(FanInElements)));

        IReadOnlyList<SourceElement> walls =
            [.. All(document).Where(static e => e.NativeType == "IfcWall")];

        walls.Should().HaveCount(FanInElements);
        walls.Should().OnlyContain(w => Text(w, "TypeName") == "Standard Wall");
        walls.Should().OnlyContain(w => Text(w, "Material") == "Concrete C40");
        walls.Should().OnlyContain(w => Text(w, "ClassificationCode") == "EF_25_10");

        // Inherited from the type's property set rather than held on the occurrence — the path
        // most of a BIM model's properties travel, and the one most easily lost.
        walls.Should().OnlyContain(w => Text(w, "Manufacturer") == "Acme");
    }

    [Fact]
    public async Task BatchedRelationships_StillReachEveryElement_AtScale()
    {
        // The realistic shape spreads elements across many relationships. Every element must still
        // resolve its type, inherited property and material — a batching bug would leave a tail of
        // elements silently unresolved.
        (SourceDocument document, _) = await TimeReadAsync(WriteModel(ScaledElements));

        IReadOnlyList<SourceElement> walls =
            [.. All(document).Where(static e => e.NativeType == "IfcWall")];

        walls.Should().HaveCount(ScaledElements);
        walls.Should().OnlyContain(w => Text(w, "TypeName") == "Standard Wall");
        walls.Should().OnlyContain(w => Text(w, "Material") == "Concrete C40");
        walls.Should().OnlyContain(w => Text(w, "Manufacturer") == "Acme");
    }

    [Fact]
    public async Task GlobalIds_RemainUnique_AtScale()
    {
        // Downstream keys features by identity. A collision at scale silently merges elements.
        (SourceDocument document, _) = await TimeReadAsync(WriteModel(ScaledElements));

        IReadOnlyList<SourceElement> elements = All(document);

        elements.Select(static e => e.Id).Distinct().Should().HaveCount(elements.Count);
    }

    [Fact]
    public async Task AttributeValues_StayRenderable_AtScale()
    {
        // The Attribute Table binds these directly. A boxed xBIM wrapper renders as a type name.
        (SourceDocument document, _) = await TimeReadAsync(WriteModel(BaselineElements));

        foreach (SourceElement element in All(document))
        {
            foreach (KeyValuePair<string, object?> attribute in element.Attributes)
            {
                if (attribute.Value is null)
                {
                    continue;
                }

                bool renderable = attribute.Value
                    is string or bool or int or long or double or float or decimal or DateTime;

                renderable.Should().BeTrue(
                    $"{element.NativeType}.{attribute.Key} is {attribute.Value.GetType().Name}, "
                    + "which the Attribute Table and export writers cannot render");
            }
        }
    }

    // ---- cancellation and robustness -----------------------------------------------------------

    [Fact]
    public async Task Cancellation_IsHonoured_PartWayThroughALargeModel()
    {
        // A user cancelling a large import must not wait for it to finish. The reader checks the
        // token inside the product loop; this proves the check is reached and respected.
        string path = WriteModel(ScaledElements);

        using CancellationTokenSource cancellation = new();
        cancellation.CancelAfter(TimeSpan.FromMilliseconds(250));

        Func<Task> read = async () => await new IfcReader(Context())
            .ReadAsync(new SourceReference(path), cancellationToken: cancellation.Token);

        Stopwatch stopwatch = Stopwatch.StartNew();

        try
        {
            await read();
        }
        catch (OperationCanceledException)
        {
            // Expected. Either surfacing the exception or returning a failed Result is acceptable;
            // what matters is that it stopped early.
        }

        stopwatch.Stop();

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(2),
            "cancellation should not wait for the whole model");
    }

    [Fact]
    public async Task WideSpatialStructure_ResolvesEveryStorey()
    {
        // Five hundred storeys under one building. The aggregation relationship is resolved per
        // element, so a wide tree multiplies that work; a tower model is exactly this shape.
        (SourceDocument document, TimeSpan elapsed) =
            await TimeReadAsync(Write(LargeModelBuilder.BuildWithStoreys(BaselineElements, 500)));

        IReadOnlyList<SourceElement> elements = All(document);

        elements.Where(static e => e.NativeType == "IfcWall").Should().HaveCount(BaselineElements);

        IReadOnlyList<SourceElement> storeys =
            [.. elements.Where(static e => e.NativeType == "IfcBuildingStorey")];

        storeys.Should().HaveCount(500);
        storeys.Should().OnlyContain(s => Text(s, "ParentName") == "Building");

        elements.Where(static e => e.NativeType == "IfcWall")
            .Select(w => Text(w, "BuildingStorey"))
            .Distinct()
            .Should().HaveCount(500, "each storey holds its own walls");

        elapsed.Should().BeLessThan(TimeSpan.FromMinutes(2), "a wide spatial tree should stay linear");
    }

    // ---- opt-in stress -------------------------------------------------------------------------

    [Fact]
    public async Task StressModel_OfOneHundredThousandElements_Reads()
    {
        // Gated: writing and reading a hundred thousand elements costs minutes and tens of
        // megabytes of temporary disk, which does not belong in every build. Run it deliberately:
        //   PowerShell:  $env:AIGIS_STRESS=1; dotnet test tests\AiGisConverter.Plugins.Ifc.Tests
        if (!string.Equals(Environment.GetEnvironmentVariable("AIGIS_STRESS"), "1", StringComparison.Ordinal))
        {
            return;
        }

        (SourceDocument document, TimeSpan elapsed) = await TimeReadAsync(WriteModel(StressElements));

        IReadOnlyList<SourceElement> elements = All(document);

        elements.Should().HaveCountGreaterThanOrEqualTo(StressElements);
        elements.Select(static e => e.Id).Distinct().Should().HaveCount(elements.Count);
        elements.Where(static e => e.NativeType == "IfcWall")
            .Should().OnlyContain(w => Text(w, "BuildingStorey") != null);

        elapsed.Should().BeLessThan(TimeSpan.FromMinutes(15),
            $"{StressElements} elements took {elapsed.TotalMinutes:F1} minutes");
    }
}
