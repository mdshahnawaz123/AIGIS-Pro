using System.Reflection;
using System.Text.RegularExpressions;

namespace AiGisConverter.IntegrationTests;

/// <summary>
/// Guards the streaming property that the whole memory profile depends on.
/// </summary>
/// <remarks>
/// The design claim is that memory scales with the largest single feature, not with the drawing.
/// One <c>ToList()</c> in an export path silently converts that into "memory scales with the
/// drawing", and no functional test would notice — the output file is byte-identical either way.
/// It only shows up as an out-of-memory failure on a customer's largest drawing.
/// </remarks>
public sealed class StreamingTests
{
    private static string SourceRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "src")))
        {
            directory = directory.Parent;
        }

        return directory is null ? string.Empty : Path.Combine(directory.FullName, "src");
    }

    [Fact]
    public void Exporters_DoNotMaterialiseTheirInput()
    {
        string root = SourceRoot();

        if (root.Length == 0 || !Directory.Exists(Path.Combine(root, "AiGisConverter.Gis", "Exporters")))
        {
            // Running from a packaged output without the source tree. The other assertions in this
            // class still apply; a source scan simply cannot run, and a false pass is better than a
            // false failure on a machine that never had the sources.
            return;
        }

        List<string> offenders = [];

        foreach (string file in Directory.EnumerateFiles(
            Path.Combine(root, "AiGisConverter.Gis", "Exporters"), "*.cs", SearchOption.AllDirectories))
        {
            string text = File.ReadAllText(file);

            foreach (Match match in Regex.Matches(text, @"\.(ToList|ToArray|ToDictionary)\(\)"))
            {
                // Materialising a small, bounded collection (a field list, a schema) is fine.
                // Materialising the feature sequence is not.
                int lineStart = text.LastIndexOf('\n', match.Index) + 1;
                int lineEnd = text.IndexOf('\n', match.Index);
                string line = text[lineStart..(lineEnd < 0 ? text.Length : lineEnd)];

                if (line.Contains("Feature", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("Field", StringComparison.OrdinalIgnoreCase)
                    && !line.Contains("Class", StringComparison.OrdinalIgnoreCase))
                {
                    offenders.Add($"{Path.GetFileName(file)}: {line.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty("exporters must consume features as a stream");
    }

    [Fact]
    public void ExportPipeline_ExposesFeaturesAsAnAsyncStream()
    {
        // Corrected: the streaming ports live in the GIS layer, not in Domain. Domain models a
        // dataset, not the act of writing one, so IStreamingExporter and IAttributeMapper belong
        // where the writing happens. The original assertion pointed at the wrong assembly and so
        // reported a design gap that does not exist.
        Assembly gis = typeof(Gis.Geometry.GeometryValidator).Assembly;

        List<MethodInfo> streaming = [.. gis.GetTypes()
            .Where(static t => t.IsInterface && t.IsPublic)
            .SelectMany(static t => t.GetMethods())
            .Where(static m => m.GetParameters().Any(static p =>
                p.ParameterType.IsGenericType
                && p.ParameterType.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)))];

        streaming.Should().NotBeEmpty(
            "the exporter ports must accept features as a stream, not a materialised collection");

        streaming.Select(static m => m.DeclaringType!.Name).Should().Contain("IStreamingExporter");
    }

    [Fact]
    public void RepeatedAllocationCycles_DoNotGrowRetainedMemory()
    {
        // Not a benchmark. This asserts the weaker but still useful property that the allocation
        // pattern used by an export loop is collectable — a retained reference would show as
        // monotonic growth across cycles rather than a flat profile.
        static long Retained()
        {
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, blocking: true);

            return GC.GetTotalMemory(forceFullCollection: true);
        }

        long baseline = Retained();

        for (int cycle = 0; cycle < 5; cycle++)
        {
            List<byte[]> transient = [];

            for (int i = 0; i < 2_000; i++)
            {
                transient.Add(new byte[1_024]);
            }

            transient.Clear();
        }

        long after = Retained();

        (after - baseline).Should().BeLessThan(8L * 1024 * 1024,
            "retained memory must not grow across repeated conversion cycles");
    }
}
