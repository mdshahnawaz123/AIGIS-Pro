using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Ifc.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Plugins.Ifc.Tests;

/// <summary>
/// End-to-end verification of the IFC reader against real, parseable IFC documents.
/// </summary>
/// <remarks>
/// These assert what the pipeline downstream depends on: that the spatial tree exists as elements,
/// that identity and property sets survive, and that the attributes the semantic provider reads are
/// actually written. Verification of an existing implementation, not a redesign of it.
/// </remarks>
public sealed class IfcReaderVerificationTests : IDisposable
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

    private string Write(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"aigis-ifc-{Guid.NewGuid():N}.ifc");
        File.WriteAllText(path, content);
        _temporary.Add(path);

        return path;
    }

    private async Task<SourceDocument> ReadAsync(string content)
    {
        Result<SourceDocument> result =
            await new IfcReader(Context()).ReadAsync(new SourceReference(Write(content)));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "the fixture must parse");

        return result.Value;
    }

    private static IReadOnlyList<SourceElement> All(SourceDocument document) =>
        [.. document.Layers.SelectMany(static layer => layer.Elements)];

    private static SourceElement? ByType(SourceDocument document, string ifcType) =>
        All(document).FirstOrDefault(e =>
            string.Equals(e.NativeType, ifcType, StringComparison.OrdinalIgnoreCase));

    // ---- format claiming ------------------------------------------------------------------

    [Fact]
    public void Reader_ClaimsTheIfcExtensions()
    {
        IfcReader reader = new(Context());

        reader.FormatKey.Should().Be("ifc");
        reader.SupportedExtensions.Should().Contain([".ifc", ".ifcxml", ".ifczip"]);
        IfcReader.IsBackendAvailable.Should().BeTrue("xBIM is bound in this build");
    }

    // ---- schema coverage ------------------------------------------------------------------

    [Fact]
    public async Task Ifc4_Parses()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        All(document).Should().NotBeEmpty();
        document.Metadata.Should().ContainKey("IfcSchema");
    }

    [Fact]
    public async Task Ifc2X3_Parses_ThroughTheSameSchemaNeutralPath()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc2X3Building);

        All(document).Should().NotBeEmpty("the reader works through Xbim.Ifc4.Interfaces for both schemas");
        ByType(document, "IfcWallStandardCase").Should().NotBeNull();
        ByType(document, "IfcBeam").Should().NotBeNull();
    }

    // ---- element coverage -----------------------------------------------------------------

    [Theory]
    [InlineData("IfcWall")]
    [InlineData("IfcSlab")]
    [InlineData("IfcColumn")]
    [InlineData("IfcDoor")]
    [InlineData("IfcSpace")]
    public async Task BuildingElements_AreRead(string ifcType)
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        ByType(document, ifcType).Should().NotBeNull($"{ifcType} is one of the types the reader claims");
    }

    // ---- spatial hierarchy ----------------------------------------------------------------

    [Theory]
    [InlineData("IfcSite")]
    [InlineData("IfcBuilding")]
    [InlineData("IfcBuildingStorey")]
    [InlineData("IfcSpace")]
    public async Task SpatialHierarchy_IsEmittedAsElements(string spatialType)
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement? element = ByType(document, spatialType);

        element.Should().NotBeNull(
            "the spatial tree must exist as elements or the semantic graph has nothing to relate to");
        element!.Attributes.Should().ContainKey("IsSpatialElement");
    }

    [Fact]
    public async Task Storey_RecordsItsParentBuilding()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement storey = ByType(document, "IfcBuildingStorey")!;

        storey.Attributes.Should().ContainKey("ParentId");
        storey.Attributes["ParentId"]!.ToString().Should().Be(IfcSamples.BuildingId);
    }

    [Fact]
    public async Task Element_RecordsTheStoreyThatContainsIt()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement wall = ByType(document, "IfcWall")!;

        wall.Attributes["BuildingStorey"]!.ToString().Should().Be("Level 1");
        wall.Attributes["ContainedInStoreyId"]!.ToString().Should().Be(IfcSamples.StoreyId);
    }

    [Fact]
    public async Task ContainedInStoreyId_ResolvesToAnEmittedStorey()
    {
        // The relationship the semantic provider builds is only real if both ends exist.
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        string storeyId = ByType(document, "IfcWall")!.Attributes["ContainedInStoreyId"]!.ToString()!;

        All(document).Should().Contain(e => e.Id == storeyId,
            "the storey a wall points at must itself be an element in the document");
    }

    // ---- host relationship ----------------------------------------------------------------

    [Fact]
    public async Task Door_RecordsTheWallItIsHostedBy()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement door = ByType(document, "IfcDoor")!;

        // A door fills an opening, and the opening voids the wall: a two-step chain.
        door.Attributes.Should().ContainKey("HostId");
        door.Attributes["HostId"]!.ToString().Should().Be(IfcSamples.WallId);
        door.Attributes["HostType"]!.ToString().Should().Be("IfcWall");
    }

    [Fact]
    public async Task HostId_ResolvesToAnEmittedElement()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        string hostId = ByType(document, "IfcDoor")!.Attributes["HostId"]!.ToString()!;

        All(document).Should().Contain(e => e.Id == hostId);
    }

    // ---- identity and properties ----------------------------------------------------------

    [Fact]
    public async Task Identity_IsPreserved()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement wall = ByType(document, "IfcWall")!;

        wall.Id.Should().Be(IfcSamples.WallId, "the GlobalId is the element's stable identity");
        wall.Attributes["GlobalId"]!.ToString().Should().Be(IfcSamples.WallId);
        wall.Attributes["Name"]!.ToString().Should().Be("Basic Wall");
        wall.Attributes["IfcType"]!.ToString().Should().Be("IfcWall");
        wall.Attributes.Should().ContainKey("ObjectType");
    }

    [Theory]
    [InlineData("FireRating")]
    [InlineData("IsExternal")]
    [InlineData("LoadBearing")]
    public async Task PropertySetValues_ArePreserved(string propertyName)
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        ByType(document, "IfcWall")!.Attributes.Should().ContainKey(propertyName,
            "property-set values are copied onto the element by name");
    }

    [Fact]
    public async Task FireRating_KeepsItsValue()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        ByType(document, "IfcWall")!.Attributes["FireRating"]!.ToString().Should().Be("REI60");
    }

    [Theory]
    [InlineData("Area")]
    [InlineData("Length")]
    [InlineData("Volume")]
    public async Task Quantities_AreMappedToTheNamesTheSemanticLayerReads(string quantity)
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        ByType(document, "IfcWall")!.Attributes.Should().ContainKey(quantity);
    }

    // ---- geometry -------------------------------------------------------------------------

    [Fact]
    public async Task Placement_IsResolvedThroughTheLocalPlacementChain()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement wall = ByType(document, "IfcWall")!;

        wall.Geometry.Should().NotBeNull();
        // Wall at (10,20) on a storey at z=3: the chain sums to the world position.
        wall.Geometry!.Coordinate.X.Should().BeApproximately(10d, 1e-6d);
        wall.Geometry!.Coordinate.Y.Should().BeApproximately(20d, 1e-6d);
    }

    [Fact]
    public async Task Geometry_IsValid_WhereverItIsPresent()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        All(document).Where(static e => e.Geometry is not null)
            .Should().OnlyContain(e => e.Geometry!.IsValid);
    }

    [Fact]
    public async Task EveryElement_CarriesTheAttributesDownstreamPanelsKeyOff()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        // No special-case code downstream: every element must be describable by these alone.
        All(document).Should().OnlyContain(e =>
            e.Attributes.ContainsKey("GlobalId") && e.Attributes.ContainsKey("IfcType"));
        All(document).Should().OnlyContain(e => !string.IsNullOrEmpty(e.NativeType));
    }

    [Fact]
    public async Task Layers_AreNamedByIfcType_SoTheProjectExplorerGroupsThem()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        document.Layers.Select(static layer => layer.Name).Should().Contain(["IfcWall", "IfcSlab", "IfcColumn"]);
    }

    // ---- failure paths --------------------------------------------------------------------

    [Fact]
    public async Task MissingFile_FailsAsAResult()
    {
        Result<SourceDocument> result = await new IfcReader(Context())
            .ReadAsync(new SourceReference(Path.Combine(Path.GetTempPath(), "no-such-model.ifc")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Ifc.FileNotFound");
    }

    [Fact]
    public async Task MalformedFile_FailsAsAResult_RatherThanThrowing()
    {
        Result<SourceDocument> result = await new IfcReader(Context())
            .ReadAsync(new SourceReference(Write("this is not an IFC file at all")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Ifc.ReadFailed");
    }
}
