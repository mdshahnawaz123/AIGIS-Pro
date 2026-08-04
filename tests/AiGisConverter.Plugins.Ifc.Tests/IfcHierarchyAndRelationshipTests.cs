using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Semantic;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Ifc.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Plugins.Ifc.Tests;

/// <summary>
/// Nested hierarchy, multi-building models, materials and the semantic graph.
/// </summary>
/// <remarks>
/// The single-building fixture cannot show whether the reader follows the tree or merely assumes
/// one of everything. These run against a campus with two buildings and three storeys, which is the
/// shape a production model actually has.
/// </remarks>
public sealed class IfcHierarchyAndRelationshipTests : IDisposable
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
        string path = Path.Combine(Path.GetTempPath(), $"aigis-ifc-{Guid.NewGuid():N}.ifc");
        File.WriteAllText(path, content);
        _temporary.Add(path);

        Result<SourceDocument> result = await new IfcReader(Context()).ReadAsync(new SourceReference(path));
        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : string.Empty);

        return result.Value;
    }

    private static IReadOnlyList<SourceElement> All(SourceDocument document) =>
        [.. document.Layers.SelectMany(static layer => layer.Elements)];

    private static IReadOnlyList<SourceElement> OfType(SourceDocument document, string ifcType) =>
        [.. All(document).Where(e => string.Equals(e.NativeType, ifcType, StringComparison.OrdinalIgnoreCase))];

    private static string? Text(SourceElement element, string name) =>
        element.Attributes.TryGetValue(name, out object? value) ? value?.ToString() : null;

    // ---- multiple buildings and storeys ----------------------------------------------------

    [Fact]
    public async Task Campus_ReadsBothBuildings()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        IReadOnlyList<SourceElement> buildings = OfType(document, "IfcBuilding");

        buildings.Should().HaveCount(2, "a reader that assumes one building fails on any real campus");
        buildings.Select(static b => Text(b, "Name")).Should().BeEquivalentTo(["Block A", "Block B"]);
    }

    [Fact]
    public async Task Campus_ReadsEveryStorey_AcrossBothBuildings()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        OfType(document, "IfcBuildingStorey").Should().HaveCount(3, "Block A has two levels, Block B has one");
    }

    [Fact]
    public async Task Storeys_ResolveToTheCorrectParentBuilding()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        IReadOnlyList<SourceElement> storeys = OfType(document, "IfcBuildingStorey");

        SourceElement levelA1 = storeys.Single(s => Text(s, "Name") == "A-Level 1");
        SourceElement levelA2 = storeys.Single(s => Text(s, "Name") == "A-Level 2");
        SourceElement levelB1 = storeys.Single(s => Text(s, "Name") == "B-Level 1");

        Text(levelA1, "ParentName").Should().Be("Block A");
        Text(levelA2, "ParentName").Should().Be("Block A");
        Text(levelB1, "ParentName").Should().Be("Block B", "the tree must not collapse to one building");
    }

    [Fact]
    public async Task Buildings_ResolveToTheSite()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        OfType(document, "IfcBuilding").Should().OnlyContain(b => Text(b, "ParentName") == "Campus Site");
    }

    [Fact]
    public async Task Walls_AreContainedByTheirOwnStorey()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        IReadOnlyList<SourceElement> walls = OfType(document, "IfcWall");

        walls.Should().HaveCount(3);
        walls.Single(w => Text(w, "Name") == "A1 Wall").Should().Match<SourceElement>(
            w => Text(w, "BuildingStorey") == "A-Level 1");
        walls.Single(w => Text(w, "Name") == "A2 Wall").Should().Match<SourceElement>(
            w => Text(w, "BuildingStorey") == "A-Level 2");
        walls.Single(w => Text(w, "Name") == "B1 Wall").Should().Match<SourceElement>(
            w => Text(w, "BuildingStorey") == "B-Level 1");
    }

    [Fact]
    public async Task EveryStoreyReferencedByAnElement_ExistsInTheDocument()
    {
        // The relationship the semantic graph builds is only real if both ends are present.
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        HashSet<string> ids = [.. All(document).Select(static e => e.Id)];

        foreach (SourceElement element in All(document))
        {
            if (Text(element, "ContainedInStoreyId") is { Length: > 0 } storeyId)
            {
                ids.Should().Contain(storeyId, $"{Text(element, "Name")} points at a storey that must exist");
            }
        }
    }

    // ---- space containment ------------------------------------------------------------------

    [Fact]
    public async Task Space_IsReadAndResolvesToItsStorey()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        SourceElement space = OfType(document, "IfcSpace").Should().ContainSingle().Subject;

        Text(space, "Name").Should().Be("A1 Room");
        Text(space, "LongName").Should().Be("Meeting Room");
        Text(space, "ParentName").Should().Be("A-Level 1", "a space is aggregated into its storey");
    }

    [Fact]
    public async Task Space_IsMarkedAsSpatial()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement space = OfType(document, "IfcSpace").Should().ContainSingle().Subject;

        space.Attributes["IsSpatialElement"].Should().Be(true);
        Text(space, "SpatialType").Should().Be("IfcSpace");
    }

    // ---- materials ---------------------------------------------------------------------------

    [Fact]
    public async Task Material_IsReadFromTheAssociation()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        IReadOnlyList<SourceElement> walls = OfType(document, "IfcWall");

        Text(walls.Single(w => Text(w, "Name") == "A1 Wall"), "Material").Should().Be("Concrete C40");
        Text(walls.Single(w => Text(w, "Name") == "B1 Wall"), "Material").Should().Be("Brick");
    }

    [Fact]
    public async Task Material_IsSharedCorrectlyAcrossElements()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        // One association covers two walls; both must resolve it, and neither may take the other's.
        OfType(document, "IfcWall")
            .Where(w => Text(w, "Material") == "Concrete C40")
            .Should().HaveCount(2);
    }

    // ---- host / opening ----------------------------------------------------------------------

    [Fact]
    public async Task Opening_IsReadAsAnElementInItsOwnRight()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        OfType(document, "IfcOpeningElement").Should().ContainSingle(
            "an opening is a real product and downstream may want to see it");
    }

    [Fact]
    public async Task Host_IsResolvedThroughTheOpening_NotAssumed()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement door = OfType(document, "IfcDoor").Single();

        Text(door, "HostId").Should().Be(IfcSamples.WallId);
        Text(door, "HostName").Should().Be("Basic Wall");
        Text(door, "HostType").Should().Be("IfcWall");
    }

    [Fact]
    public async Task ElementsWithNoOpening_CarryNoHost()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        // A slab fills no opening; inventing a host would be worse than leaving it absent.
        OfType(document, "IfcSlab").Single().Attributes.Should().NotContainKey("HostId");
    }

    // ---- IFC2x3 parity -----------------------------------------------------------------------

    [Fact]
    public async Task Legacy_SpatialHierarchy_IsReadTheSameWay()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc2X3Building);

        OfType(document, "IfcSite").Should().ContainSingle();
        OfType(document, "IfcBuilding").Should().ContainSingle();
        OfType(document, "IfcBuildingStorey").Should().ContainSingle();

        Text(OfType(document, "IfcBuildingStorey").Single(), "ParentName").Should().Be("Legacy Building");
    }

    [Fact]
    public async Task Legacy_Containment_IsRead()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc2X3Building);

        Text(OfType(document, "IfcWallStandardCase").Single(), "BuildingStorey").Should().Be("Legacy Level");
        Text(OfType(document, "IfcBeam").Single(), "BuildingStorey").Should().Be("Legacy Level");
    }

    // ---- semantic graph ----------------------------------------------------------------------

    [Fact]
    public async Task SemanticGraph_IsPopulated_WithContainsRelationships()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SemanticGraph graph = await new IfcSemanticProvider()
            .ExtractSemanticsAsync(ToAsync(All(document)));

        graph.Features.Should().NotBeEmpty();

        // The storey must now hold Contains relationships: before the spatial elements were
        // emitted, this graph came back with none at all.
        SemanticFeature storey = graph.Features.Single(f => f.Id == IfcSamples.StoreyId);

        storey.Relationships.Should().NotBeEmpty("the storey contains the elements placed on it");
        storey.Relationships.Should().OnlyContain(
            r => r.RelationshipType == SemanticRelationshipType.Contains);
    }

    [Fact]
    public async Task SemanticGraph_CarriesTheBimProperties()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SemanticGraph graph = await new IfcSemanticProvider()
            .ExtractSemanticsAsync(ToAsync(All(document)));

        SemanticFeature wall = graph.Features.Single(f => f.Id == IfcSamples.WallId);

        wall.Category.Should().Be("IfcWall");
        wall.Level.Should().Be("Level 1");
        wall.Area.Should().BeApproximately(15.5d, 1e-6d);
        wall.Volume.Should().BeApproximately(3.1d, 1e-6d);
    }

    private static async IAsyncEnumerable<SourceElement> ToAsync(IEnumerable<SourceElement> elements)
    {
        foreach (SourceElement element in elements)
        {
            yield return element;
            await Task.CompletedTask;
        }
    }
}
