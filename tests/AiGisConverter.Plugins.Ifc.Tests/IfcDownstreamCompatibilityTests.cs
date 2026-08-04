using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Semantic;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Ifc.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Plugins.Ifc.Tests;

/// <summary>
/// Verifies the IFC reader's output against what the rest of the application actually requires.
/// </summary>
/// <remarks>
/// <para>
/// The plugin cannot reference the Mapping Editor, QA/QC or GIS assemblies — plugins load through
/// the host's isolated context and reference only Domain and the plugin contracts. So this asserts
/// the same thing from the other side: the <see cref="SourceElement"/> and
/// <see cref="SemanticGraph"/> contract each consumer relies on, with the consumer named against
/// every assertion so a future change can see what it would break.
/// </para>
/// <para>
/// These failures are quiet ones. A null <c>Category</c> does not throw; it just means no
/// classification rule ever matches and the Statistics panel reports zero. Silence is what makes
/// them worth pinning.
/// </para>
/// </remarks>
public sealed class IfcDownstreamCompatibilityTests : IDisposable
{
    private readonly List<string> _temporary = [];

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

    private async Task<SourceDocument> ReadAsync(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aigis-ifc-compat-{Guid.NewGuid():N}.ifc");
        File.WriteAllText(path, content);
        _temporary.Add(path);

        Result<SourceDocument> result = await new IfcReader(Context()).ReadAsync(new SourceReference(path));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);

        return result.Value;
    }

    private static IReadOnlyList<SourceElement> All(SourceDocument document) =>
        [.. document.Layers.SelectMany(static layer => layer.Elements)];

    private static async Task<SemanticGraph> GraphAsync(SourceDocument document) =>
        await new IfcSemanticProvider().ExtractSemanticsAsync(ToAsync(All(document)));

    private static async IAsyncEnumerable<SourceElement> ToAsync(IEnumerable<SourceElement> elements)
    {
        foreach (SourceElement element in elements)
        {
            yield return element;
            await Task.CompletedTask;
        }
    }

    // ---- classification rules ------------------------------------------------------------------

    [Fact]
    public async Task EveryFeature_HasACategory_SoClassificationRulesCanMatch()
    {
        // ClassificationEngine: rule.EntityTypes.Contains(feature.Category). A null Category is not
        // an error — it simply never matches, and every rule silently does nothing.
        SemanticGraph graph = await GraphAsync(await ReadAsync(IfcSamples.Ifc4Building));

        graph.Features.Should().OnlyContain(f => !string.IsNullOrWhiteSpace(f.Category));
    }

    [Fact]
    public async Task Category_IsTheIfcClassName_WhichIsWhatTheRulesAreWrittenAgainst()
    {
        // MissingHostRule tests Category == "IfcDoor" || "IfcWindow" literally.
        SemanticGraph graph = await GraphAsync(await ReadAsync(IfcSamples.Ifc4Building));

        IReadOnlyList<string> categories =
            [.. graph.Features.Select(static f => f.Category).OfType<string>()];

        categories.Should().Contain("IfcWall").And.Contain("IfcDoor").And.Contain("IfcSlab");
    }

    [Fact]
    public async Task DoorsThatFillAnOpening_CarryAHost_SoMissingHostRuleDoesNotFalselyFire()
    {
        SemanticGraph graph = await GraphAsync(await ReadAsync(IfcSamples.Ifc4Building));

        SemanticFeature door = graph.Features.Single(static f => f.Category == "IfcDoor");

        door.RawSource.Attributes.Should().ContainKey("HostId");
    }

    // ---- FeatureBuilder / export attributes -----------------------------------------------------

    [Fact]
    public async Task CategoryAndLevel_AreNonWhitespace_SoFeatureBuilderEmitsThem()
    {
        // FeatureBuilder guards on !string.IsNullOrEmpty before writing SemanticCategory and
        // SemanticLevel. A whitespace-only value passes the guard and exports as a blank column.
        SemanticGraph graph = await GraphAsync(await ReadAsync(IfcSamples.Ifc4Campus));

        foreach (SemanticFeature feature in graph.Features)
        {
            if (feature.Category is not null)
            {
                feature.Category.Should().NotBeNullOrWhiteSpace();
            }

            if (feature.Level is not null)
            {
                feature.Level.Should().NotBeNullOrWhiteSpace();
            }
        }
    }

    [Fact]
    public async Task EveryElementId_IsPresentAndUnique_BecauseExportKeysOnIt()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        IReadOnlyList<SourceElement> elements = All(document);

        elements.Should().OnlyContain(e => !string.IsNullOrWhiteSpace(e.Id));
        elements.Select(static e => e.Id).Distinct().Should().HaveCount(elements.Count);
    }

    [Fact]
    public async Task SemanticFeatureIds_MatchTheirSourceElements_SoSelectionSyncResolves()
    {
        // The Mapping Editor synchronises selection between the map and the Attribute Table by id.
        // If the semantic id and the source id ever diverged, selection would silently do nothing.
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);
        SemanticGraph graph = await GraphAsync(document);

        HashSet<string> sourceIds = [.. All(document).Select(static e => e.Id)];

        graph.Features.Should().OnlyContain(f => sourceIds.Contains(f.Id));
        graph.Features.Should().OnlyContain(f => f.Id == f.RawSource.Id);
    }

    // ---- statistics -----------------------------------------------------------------------------

    [Fact]
    public async Task Quantities_AreFiniteNumbers_SoStatisticsDoNotProduceNaN()
    {
        // The Statistics panel sums these. One NaN poisons the total for the whole model.
        SemanticGraph graph = await GraphAsync(await ReadAsync(IfcSamples.Ifc4TypesAndProperties));

        foreach (SemanticFeature feature in graph.Features)
        {
            double?[] quantities = [feature.Area, feature.Volume, feature.Length, feature.Elevation];

            foreach (double? quantity in quantities)
            {
                if (quantity.HasValue)
                {
                    double.IsFinite(quantity.Value).Should().BeTrue(
                        $"{feature.Category} carries a non-finite quantity");
                }
            }
        }
    }

    // ---- project explorer -----------------------------------------------------------------------

    [Fact]
    public async Task SpatialElements_AreEmitted_SoProjectExplorerHasATreeToDraw()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        IReadOnlyList<SourceElement> spatial =
            [.. All(document).Where(static e =>
                e.Attributes.TryGetValue("IsSpatialElement", out object? v) && v is true)];

        spatial.Should().NotBeEmpty("the tree is built from site, building and storey elements");
        IReadOnlyList<string> types = [.. spatial.Select(static e => e.NativeType).OfType<string>()];

        types.Should().Contain("IfcSite").And.Contain("IfcBuilding").And.Contain("IfcBuildingStorey");
    }

    [Fact]
    public async Task EveryParentReference_ResolvesToAnElement_ExceptTheProjectRoot()
    {
        // A ParentId pointing at nothing puts an orphan branch in the tree — with exactly one
        // legitimate exception. The site is aggregated into the IfcProject, and IfcProject is not
        // an IfcProduct, so the reader never emits it as an element. That single unresolved
        // reference is the root of the tree, not an orphan. Asserting "at most one distinct
        // unresolved parent" keeps the orphan check sharp while allowing the root.
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        IReadOnlyList<SourceElement> elements = All(document);
        HashSet<string> ids = [.. elements.Select(static e => e.Id)];

        IReadOnlyList<string> unresolved = [.. Unresolved(elements, ids)];

        unresolved.Distinct().Should().HaveCountLessThanOrEqualTo(1,
            "the only parent that may be absent is the IfcProject root");

        // And whatever is unresolved must belong to a spatial root, never to an ordinary element.
        foreach (SourceElement element in elements)
        {
            if (element.Attributes.TryGetValue("ParentId", out object? parent)
                && parent?.ToString() is { Length: > 0 } parentId
                && !ids.Contains(parentId))
            {
                element.NativeType.Should().Be("IfcSite",
                    "only the site sits directly under the project");
            }
        }
    }

    /// <summary>Lists parent references that name an element the document does not contain.</summary>
    /// <param name="elements">The elements read from the model.</param>
    /// <param name="ids">Every element id in the document.</param>
    /// <returns>The unresolved parent ids, with duplicates preserved.</returns>
    private static IEnumerable<string> Unresolved(
        IReadOnlyList<SourceElement> elements,
        HashSet<string> ids)
    {
        foreach (SourceElement element in elements)
        {
            if (element.Attributes.TryGetValue("ParentId", out object? parent)
                && parent?.ToString() is { Length: > 0 } parentId
                && !ids.Contains(parentId))
            {
                yield return parentId;
            }
        }
    }

    // ---- semantic graph integrity ----------------------------------------------------------------

    [Fact]
    public async Task EveryRelationship_PointsAtFeaturesThatExist()
    {
        SemanticGraph graph = await GraphAsync(await ReadAsync(IfcSamples.Ifc4Campus));

        HashSet<string> ids = [.. graph.Features.Select(static f => f.Id)];

        foreach (SemanticFeature feature in graph.Features)
        {
            foreach (SemanticRelationship relationship in feature.Relationships)
            {
                ids.Should().Contain(relationship.SourceFeatureId);
                ids.Should().Contain(relationship.TargetFeatureId);
            }
        }
    }

    [Fact]
    public async Task ContainmentRelationships_CoverEveryContainedElement()
    {
        // The guard in the provider skips a relationship whose target is missing. That is correct,
        // but it also means a wholesale failure to resolve storeys would show up as an empty tree
        // rather than an error, so the count is asserted rather than merely "not empty".
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);
        SemanticGraph graph = await GraphAsync(document);

        int expected = All(document)
            .Count(static e => e.Attributes.TryGetValue("ContainedInStoreyId", out object? v)
                && !string.IsNullOrEmpty(v?.ToString()));

        int actual = graph.Features
            .SelectMany(static f => f.Relationships)
            .Count(static r => r.RelationshipType == SemanticRelationshipType.Contains);

        expected.Should().BeGreaterThan(0, "the campus fixture places walls on storeys");
        actual.Should().Be(expected);
    }

    [Fact]
    public async Task HostRelationships_AreRecordedBothWays_ForReverseSelectionSync()
    {
        SemanticGraph graph = await GraphAsync(await ReadAsync(IfcSamples.Ifc4Building));

        SemanticFeature door = graph.Features.Single(static f => f.Category == "IfcDoor");
        SemanticFeature wall = graph.Features.Single(static f => f.Id == IfcSamples.WallId);

        wall.Relationships.Should().Contain(r =>
            r.RelationshipType == SemanticRelationshipType.Hosts && r.TargetFeatureId == door.Id);

        door.Relationships.Should().Contain(r =>
            r.RelationshipType == SemanticRelationshipType.BelongsTo && r.TargetFeatureId == wall.Id);
    }

    // ---- units ------------------------------------------------------------------------------------

    [Fact]
    public async Task UnitsAreRecordedOnTheDocument_SoLengthsAreNotAmbiguous()
    {
        // Without these a wall "3.0" long is three metres or three millimetres, and the Attribute
        // Table has no way to say which.
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        document.Units.Should().NotBeNullOrWhiteSpace();
        document.Metadata.Should().ContainKey("LengthUnit");
        document.Metadata["LengthUnit"].Should().NotBeNullOrWhiteSpace();
    }

    // ---- attribute table --------------------------------------------------------------------------

    [Fact]
    public async Task EveryAttributeValue_IsAPrimitiveTheAttributeTableCanRender()
    {
        foreach (string sample in new[]
        {
            IfcSamples.Ifc4Building,
            IfcSamples.Ifc4Campus,
            IfcSamples.Ifc4TypesAndProperties,
            IfcSamples.Ifc2X3Building,
        })
        {
            SourceDocument document = await ReadAsync(sample);

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
                        + "which binds as a type name rather than a value");
                }
            }
        }
    }
}
