using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Ifc.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Plugins.Ifc.Tests;

/// <summary>
/// Type objects, inherited and nested property sets, classifications, quantities and units.
/// </summary>
/// <remarks>
/// These cover where BIM data actually lives. A door's fire rating is usually on its type, its
/// frame details inside a nested set, and its meaning in a classification reference — none of which
/// a reader that only walks the occurrence's flat property sets would ever see.
/// </remarks>
public sealed class IfcPropertyAndTypeTests : IDisposable
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

    private async Task<SourceElement> DoorAsync()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4TypesAndProperties);

        return document.Layers.SelectMany(static layer => layer.Elements)
            .Single(static e => e.NativeType == "IfcDoor");
    }

    private static string? Text(SourceElement element, string name) =>
        element.Attributes.TryGetValue(name, out object? value) ? value?.ToString() : null;

    // ---- type objects -----------------------------------------------------------------------

    [Fact]
    public async Task TypeObject_IsRecordedOnTheOccurrence()
    {
        SourceElement door = await DoorAsync();

        Text(door, "TypeName").Should().Be("Single Flush 900");
        Text(door, "TypeIfcClass").Should().Be("IfcDoorType");
        Text(door, "TypeGlobalId").Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ElementWithNoType_CarriesNoTypeAttributes()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        SourceElement wall = document.Layers.SelectMany(static l => l.Elements)
            .Single(static e => e.NativeType == "IfcWall");

        wall.Attributes.Should().NotContainKey("TypeName", "inventing a type would be worse than none");
    }

    // ---- inherited property sets ------------------------------------------------------------

    [Theory]
    [InlineData("Manufacturer", "Acme")]
    [InlineData("AcousticRating", "45dB")]
    public async Task PropertiesOnTheType_AreInheritedByTheOccurrence(string property, string expected)
    {
        SourceElement door = await DoorAsync();

        // Most BIM properties sit on the type; reading only the occurrence loses the majority.
        Text(door, property).Should().Be(expected);
    }

    [Fact]
    public async Task PropertiesOnTheOccurrence_AreAlsoRead()
    {
        SourceElement door = await DoorAsync();

        Text(door, "FireRating").Should().Be("EI30");
    }

    // ---- nested property sets ---------------------------------------------------------------

    [Theory]
    [InlineData("Status")]
    [InlineData("NestedDepth")]
    public async Task NestedComplexProperties_AreFlattenedToTheirLeaves(string property)
    {
        SourceElement door = await DoorAsync();

        // IfcComplexProperty groups values; the leaves carry the data, so recursion is required.
        door.Attributes.Should().ContainKey(property);
    }

    [Fact]
    public async Task NestedProperty_KeepsItsValue()
    {
        SourceElement door = await DoorAsync();

        Text(door, "Status").Should().Be("New");
    }

    // ---- classification ---------------------------------------------------------------------

    [Fact]
    public async Task ClassificationReference_IsRead()
    {
        SourceElement door = await DoorAsync();

        Text(door, "ClassificationCode").Should().Be("EF_25_10");
        Text(door, "ClassificationName").Should().Be("Doors");
    }

    [Fact]
    public async Task ClassificationSystem_IsResolvedFromTheReferencedSource()
    {
        SourceElement door = await DoorAsync();

        Text(door, "ClassificationSystem").Should().Be("Uniclass 2015");
    }

    // ---- quantity sets -----------------------------------------------------------------------

    [Fact]
    public async Task QuantitySet_IsMappedToTheCanonicalNames()
    {
        SourceElement door = await DoorAsync();

        // The quantity is named NetArea in the file; the reader maps by measure type, not by name.
        door.Attributes["Area"].Should().Be(1.89d);
        door.Attributes["Length"].Should().Be(6.0d);
    }

    // ---- unit assignment ---------------------------------------------------------------------

    [Fact]
    public async Task UnitAssignment_IsRecorded_IncludingThePrefix()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4TypesAndProperties);

        // A length of "120" is 120 millimetres or 120 metres depending only on this declaration.
        document.Units.Should().Be("millimetre");
        document.Metadata["LengthUnit"].Should().Be("millimetre");
    }

    [Theory]
    [InlineData("AreaUnit", "square_metre")]
    [InlineData("VolumeUnit", "cubic_metre")]
    [InlineData("AngleUnit", "radian")]
    public async Task DerivedUnits_AreRecorded(string key, string expected)
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4TypesAndProperties);

        document.Metadata[key].Should().Be(expected);
    }

    [Fact]
    public async Task UnprefixedLengthUnit_IsRecordedPlainly()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        document.Units.Should().Be("metre");
    }

    // ---- placement hierarchy -----------------------------------------------------------------

    [Fact]
    public async Task PlacementChain_AccumulatesThroughEveryLevel()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Building);

        // Door at (11,20) relative to a wall at (10,20) on a storey at z=3: the chain sums.
        SourceElement door = document.Layers.SelectMany(static l => l.Elements)
            .Single(static e => e.NativeType == "IfcDoor");

        door.Geometry.Should().NotBeNull();
        door.Geometry!.Coordinate.X.Should().BeApproximately(32d, 1e-6d,
            "11 relative to the opening, itself 11 relative to the wall at 10");
        door.Geometry!.Coordinate.Y.Should().BeApproximately(60d, 1e-6d);
    }

    [Fact]
    public async Task PlacementIsRelativeToTheStorey_NotTheWorldOrigin()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        // The A2 wall sits at (2,2) on a storey 4m up; x and y come only from the chain.
        SourceElement wall = document.Layers.SelectMany(static l => l.Elements)
            .Single(e => e.NativeType == "IfcWall" && Text(e, "Name") == "A2 Wall");

        wall.Geometry!.Coordinate.X.Should().BeApproximately(2d, 1e-6d);
        wall.Geometry!.Coordinate.Y.Should().BeApproximately(2d, 1e-6d);
    }

    [Fact]
    public async Task BuildingOffset_ReachesItsElements()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4Campus);

        // Block B is offset 500m east; its wall must inherit that through the chain.
        SourceElement wall = document.Layers.SelectMany(static l => l.Elements)
            .Single(e => e.NativeType == "IfcWall" && Text(e, "Name") == "B1 Wall");

        wall.Geometry!.Coordinate.X.Should().BeApproximately(503d, 1e-6d,
            "3 metres into a building that is itself 500 metres east");
    }

    // ---- downstream compatibility --------------------------------------------------------------

    [Fact]
    public async Task AllAttributeValues_AreTypesTheAttributeTableCanRender()
    {
        SourceDocument document = await ReadAsync(IfcSamples.Ifc4TypesAndProperties);

        foreach (SourceElement element in document.Layers.SelectMany(static l => l.Elements))
        {
            foreach (KeyValuePair<string, object?> attribute in element.Attributes)
            {
                if (attribute.Value is null)
                {
                    continue;
                }

                // The schema factory maps these; an exotic type would surface as an opaque string.
                attribute.Value.Should().Match(v =>
                    v is string || v is bool || v is int || v is long || v is double || v is float
                    || v is decimal || v is DateTime || v is DateTimeOffset,
                    $"{attribute.Key} must be a value the GIS attribute schema understands");
            }
        }
    }
}
