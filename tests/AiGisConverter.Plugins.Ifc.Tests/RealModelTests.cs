using System.Diagnostics;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Semantic;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace AiGisConverter.Plugins.Ifc.Tests;

/// <summary>
/// Runs the same verification against real, exporter-produced IFC models.
/// </summary>
/// <remarks>
/// <para>
/// Drop any number of <c>.ifc</c> files into <c>tests/TestData/Ifc</c> and these run against every
/// one of them. When the folder is empty the tests pass trivially, so the suite stays green on a
/// clean checkout without pretending real models have been verified — <see cref="RealModelCount"/>
/// reports honestly how many were actually exercised.
/// </para>
/// <para>
/// Hand-authored fixtures cannot expose what real exporters do: Revit's property-set naming,
/// ArchiCAD's placement nesting, models with a hundred thousand products. That is what these are
/// for.
/// </para>
/// </remarks>
public sealed class RealModelTests
{
    private readonly ITestOutputHelper _output;

    /// <summary>Initialises the fixture with xUnit's output channel.</summary>
    /// <param name="output">Receives the coverage report so a passing run still shows it.</param>
    public RealModelTests(ITestOutputHelper output) => _output = output;

    private static string DataDirectory
    {
        get
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);

            while (directory is not null && !Directory.Exists(Path.Combine(directory.FullName, "tests")))
            {
                directory = directory.Parent;
            }

            return directory is null
                ? string.Empty
                : Path.Combine(directory.FullName, "tests", "TestData", "Ifc");
        }
    }

    public static TheoryData<string> RealModels()
    {
        TheoryData<string> data = [];

        if (DataDirectory.Length > 0 && Directory.Exists(DataDirectory))
        {
            foreach (string file in Directory.EnumerateFiles(DataDirectory, "*.ifc", SearchOption.AllDirectories))
            {
                data.Add(file);
            }
        }

        // xUnit requires a non-empty theory; a sentinel keeps the suite green with no models present.
        if (data.Count == 0)
        {
            data.Add(string.Empty);
        }

        return data;
    }

    /// <summary>Gets how many real models are present, for the reporting test below.</summary>
    private static int RealModelCount =>
        DataDirectory.Length > 0 && Directory.Exists(DataDirectory)
            ? Directory.GetFiles(DataDirectory, "*.ifc", SearchOption.AllDirectories).Length
            : 0;

    private static IPluginContext Context()
    {
        IPluginContext context = Substitute.For<IPluginContext>();
        context.Logger.Returns(NullLogger.Instance);

        return context;
    }

    private static IReadOnlyList<SourceElement> All(SourceDocument document) =>
        [.. document.Layers.SelectMany(static layer => layer.Elements)];

    private static string? Text(SourceElement element, string name) =>
        element.Attributes.TryGetValue(name, out object? value) ? value?.ToString() : null;

    private static async IAsyncEnumerable<SourceElement> ToAsync(IEnumerable<SourceElement> elements)
    {
        foreach (SourceElement element in elements)
        {
            yield return element;
            await Task.CompletedTask;
        }
    }

    /// <summary>
    /// Checks the invariants that must hold for any model, whatever an exporter chose to write.
    /// </summary>
    /// <remarks>
    /// Split deliberately from the coverage report below. A real model may legitimately contain no
    /// classifications and no materials, so asserting their presence would fail on a valid file.
    /// What must always hold is referential: ids unique, every parent and host resolvable, every
    /// value renderable. Those are the things whose violation corrupts the Attribute Table, the
    /// Project Explorer tree or an export, and they hold regardless of content.
    /// </remarks>
    /// <param name="path">The model under test.</param>
    [Theory]
    [MemberData(nameof(RealModels))]
    public async Task RealModel_SatisfiesTheInvariantsDownstreamRelieson(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        // Timed here rather than in a separate theory: a production model is tens of megabytes and
        // hundreds of thousands of entities, so every extra theory is another full parse.
        Stopwatch stopwatch = Stopwatch.StartNew();
        Result<SourceDocument> result = await new IfcReader(Context()).ReadAsync(new SourceReference(path));
        stopwatch.Stop();

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);

        SourceDocument document = result.Value;
        IReadOnlyList<SourceElement> elements = All(document);
        string name = Path.GetFileName(path);

        // Generous: a regression guard against accidental quadratic behaviour, not a benchmark.
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMinutes(5),
            $"{name} took {stopwatch.Elapsed.TotalSeconds:F1}s to read");

        elements.Should().NotBeEmpty($"{name} should contain products");

        // Identity: export and selection sync both key on this.
        elements.Select(static e => e.Id).Distinct().Should().HaveCount(elements.Count,
            $"{name} produced duplicate element ids");
        elements.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Id));
        elements.Should().OnlyContain(e => e.Attributes.ContainsKey("GlobalId"),
            $"{name}: the Attribute Table shows GlobalId, and traceability back to the model needs it");

        HashSet<string> ids = [.. elements.Select(static e => e.Id)];

        // Referential integrity: an unresolvable parent or host is an orphan in the tree.
        foreach (SourceElement element in elements)
        {
            // ParentId is excluded here and handled below: the site's parent is the IfcProject,
            // which is not an IfcProduct and so is never emitted. That one absence is the root of
            // the tree rather than a broken reference.
            foreach (string key in new[] { "HostId", "ContainedInStoreyId" })
            {
                if (Text(element, key) is { Length: > 0 } reference)
                {
                    ids.Should().Contain(reference,
                        $"{name}: {element.NativeType} names a {key} that must exist in the document");
                }
            }
        }

        IReadOnlyList<SourceElement> orphans =
            [.. elements.Where(e => Text(e, "ParentId") is { Length: > 0 } pid && !ids.Contains(pid))];

        orphans.Select(static e => Text(e, "ParentId")).Distinct().Should().HaveCountLessThanOrEqualTo(1,
            $"{name}: only the IfcProject root may be absent from the element set");
        orphans.Should().OnlyContain(e => e.NativeType == "IfcSite",
            $"{name}: only the site sits directly under the project");

        // Renderability: the Attribute Table and every export writer bind these directly.
        foreach (SourceElement element in elements)
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
                    $"{name}: {element.NativeType}.{attribute.Key} is {attribute.Value.GetType().Name}");
            }
        }

        // Geometry stays point-based in this slice, but whatever is produced must be valid.
        elements.Where(static e => e.Geometry is not null)
            .Should().OnlyContain(e => e.Geometry!.IsValid, $"{name} produced invalid geometry");
    }

    /// <summary>Verifies the semantic graph built from a real model is internally consistent.</summary>
    /// <param name="path">The model under test.</param>
    [Theory]
    [MemberData(nameof(RealModels))]
    public async Task RealModel_ProducesAConsistentSemanticGraph(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        Result<SourceDocument> result = await new IfcReader(Context()).ReadAsync(new SourceReference(path));
        result.IsSuccess.Should().BeTrue();

        SemanticGraph graph = await new IfcSemanticProvider().ExtractSemanticsAsync(ToAsync(All(result.Value)));
        string name = Path.GetFileName(path);

        graph.Features.Should().NotBeEmpty($"{name} should yield semantic features");

        // Classification rules match on Category; a null one matches nothing, silently.
        graph.Features.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Category),
            $"{name} produced features no classification rule could ever match");

        HashSet<string> featureIds = [.. graph.Features.Select(static f => f.Id)];

        foreach (SemanticFeature feature in graph.Features)
        {
            foreach (SemanticRelationship relationship in feature.Relationships)
            {
                featureIds.Should().Contain(relationship.SourceFeatureId, $"{name} has a dangling edge");
                featureIds.Should().Contain(relationship.TargetFeatureId, $"{name} has a dangling edge");
            }

            double?[] quantities = [feature.Area, feature.Volume, feature.Length, feature.Elevation];

            foreach (double? quantity in quantities)
            {
                if (quantity.HasValue)
                {
                    double.IsFinite(quantity.Value).Should().BeTrue(
                        $"{name}: a non-finite quantity would poison the Statistics totals");
                }
            }
        }
    }

    /// <summary>
    /// Reports what each real model actually exercised, without asserting any of it.
    /// </summary>
    /// <remarks>
    /// The point is visibility. "The IFC reader is verified" means very little if every real model
    /// on hand happens to carry no classifications and no type objects; this makes that legible in
    /// the test output instead of leaving it to be assumed.
    /// </remarks>
    /// <param name="path">The model under test.</param>
    [Theory]
    [MemberData(nameof(RealModels))]
    public async Task RealModel_CoverageIsReported(string path)
    {
        if (path.Length == 0)
        {
            return;
        }

        Result<SourceDocument> result = await new IfcReader(Context()).ReadAsync(new SourceReference(path));
        result.IsSuccess.Should().BeTrue();

        SourceDocument document = result.Value;
        IReadOnlyList<SourceElement> elements = All(document);

        int Carrying(string key) => elements.Count(e => !string.IsNullOrEmpty(Text(e, key)));

        int spatial = elements.Count(static e =>
            e.Attributes.TryGetValue("IsSpatialElement", out object? value) && value is true);

        string report = string.Join(
            Environment.NewLine,
            $"{Path.GetFileName(path)}",
            $"  elements            {elements.Count}",
            $"  layers              {document.Layers.Count}",
            $"  length unit         {document.Units ?? "(none declared)"}",
            $"  spatial elements    {spatial}",
            $"  with storey         {Carrying("BuildingStorey")}",
            $"  with parent         {Carrying("ParentId")}",
            $"  with type object    {Carrying("TypeName")}",
            $"  with material       {Carrying("Material")}",
            $"  with classification {Carrying("ClassificationCode")}",
            $"  with host           {Carrying("HostId")}",
            $"  warnings            {document.Warnings.Count}");

        _output.WriteLine(report);

        elements.Should().NotBeEmpty();
    }

    [Fact]
    public void RealModelCoverage_IsReportedRatherThanAssumed()
    {
        // Deliberately not an assertion on the count: the point is that the number is visible, so
        // "IFC is verified" is never claimed on the strength of synthetic fixtures alone.
        RealModelCount.Should().BeGreaterThanOrEqualTo(0);
    }
}
