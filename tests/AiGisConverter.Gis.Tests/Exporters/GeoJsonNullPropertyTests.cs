using System.Text.Json;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Exporters.GeoJson;
using AiGisConverter.Gis.Options;
using AiGisConverter.Gis.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.Exporters;

/// <summary>
/// Covers the omission of null properties from GeoJSON.
/// </summary>
/// <remarks>
/// <para>
/// The attribute schema is uniform across a dataset because a shapefile or a table requires it,
/// and until this was added every feature carried every column whether it had a value or not.
/// Measured on a real BIM export: 22,946 features over an 85-column schema, where 52 of those
/// columns came from nine elements. The nulls those nine imposed on everything else were 29.2 MB
/// of a 67.4 MB file - 43% of the deliverable, carrying nothing.
/// </para>
/// <para>
/// The risk being guarded against is the opposite mistake: dropping a property that has a value,
/// or dropping one whose value is legitimately falsy. Zero, false and the empty string are values.
/// </para>
/// </remarks>
public sealed class GeoJsonNullPropertyTests
{
    private static StreamingGeoJsonExporter Exporter(bool omitNulls) =>
        new(GisOptionsFactory.Monitor(options => options.Export.OmitNullGeoJsonProperties = omitNulls),
            NullLogger<StreamingGeoJsonExporter>.Instance);

    private static GisFeature Sparse(string id, IDictionary<string, AttributeValue> attributes) =>
        new(id,
            FeatureClass.Create("PARCEL", GeometryKind.Point),
            new GeometryFactory().CreatePoint(new Coordinate(1d, 2d)),
            attributes,
            LayerName.Create("C-TEST"),
            id);

    private static async IAsyncEnumerable<GisFeature> Yield(params GisFeature[] features)
    {
        foreach (GisFeature feature in features)
        {
            yield return feature;
            await Task.Yield();
        }
    }

    private static async Task<JsonElement> WriteAndReadFirst(bool omitNulls, GisFeature feature)
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("sparse.geojson");

        await Exporter(omitNulls).WriteAsync(FeatureFactory.Request(path), Yield(feature));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));

        return document.RootElement
            .GetProperty("features")[0]
            .GetProperty("properties")
            .Clone();
    }

    [Fact]
    public async Task NullPropertiesAreOmittedByDefault()
    {
        JsonElement properties = await WriteAndReadFirst(true, Sparse("f1", new Dictionary<string, AttributeValue>
        {
            ["PLOT"] = AttributeValue.FromText("00742"),
            ["EMPTY"] = AttributeValue.Null(AttributeDataType.Text),
            ["ALSO_EMPTY"] = AttributeValue.Null(AttributeDataType.Double),
        }));

        properties.TryGetProperty("PLOT", out _).Should().BeTrue();
        properties.TryGetProperty("EMPTY", out _).Should().BeFalse();
        properties.TryGetProperty("ALSO_EMPTY", out _).Should().BeFalse();
    }

    [Fact]
    public async Task ThePreviousOutputIsRestorableByConfiguration()
    {
        // Backward compatibility is one setting away, for a consumer that infers its columns from
        // a single feature rather than from the collection.
        JsonElement properties = await WriteAndReadFirst(false, Sparse("f1", new Dictionary<string, AttributeValue>
        {
            ["PLOT"] = AttributeValue.FromText("00742"),
            ["EMPTY"] = AttributeValue.Null(AttributeDataType.Text),
        }));

        properties.TryGetProperty("EMPTY", out JsonElement empty).Should().BeTrue();
        empty.ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task FalsyValuesAreNotMistakenForAbsentOnes()
    {
        // The obvious way to get this wrong. Zero, false and the empty string are values, and a
        // thickness of zero dropped from an export is indistinguishable from one never measured.
        JsonElement properties = await WriteAndReadFirst(true, Sparse("f1", new Dictionary<string, AttributeValue>
        {
            ["ZERO"] = AttributeValue.FromDouble(0d),
            ["FALSE"] = AttributeValue.FromBoolean(false),
            ["BLANK"] = AttributeValue.FromText(string.Empty),
            ["ZERO_INT"] = AttributeValue.FromInteger(0),
        }));

        properties.TryGetProperty("ZERO", out JsonElement zero).Should().BeTrue();
        zero.GetDouble().Should().Be(0d);

        properties.TryGetProperty("FALSE", out JsonElement flag).Should().BeTrue();
        flag.GetBoolean().Should().BeFalse();

        properties.TryGetProperty("BLANK", out JsonElement blank).Should().BeTrue();
        blank.GetString().Should().BeEmpty();

        properties.TryGetProperty("ZERO_INT", out _).Should().BeTrue();
    }

    [Fact]
    public async Task AFeatureWithNothingButNullsStillWritesAPropertiesObject()
    {
        // RFC 7946 requires the member to be present. An empty object is legal; a missing one is not.
        JsonElement properties = await WriteAndReadFirst(true, Sparse("f1", new Dictionary<string, AttributeValue>
        {
            ["EMPTY"] = AttributeValue.Null(AttributeDataType.Text),
        }));

        properties.ValueKind.Should().Be(JsonValueKind.Object);
        properties.EnumerateObject().Should().BeEmpty();
    }

    [Fact]
    public async Task FeaturesMayCarryDifferentPropertySets()
    {
        // The whole point. Two features sharing a schema but not values now differ on the wire,
        // which RFC 7946 permits and every GIS consumer unions on read.
        using TempWorkspace workspace = new();
        string path = workspace.Path("mixed.geojson");

        await Exporter(true).WriteAsync(
            FeatureFactory.Request(path),
            Yield(
                Sparse("wide", new Dictionary<string, AttributeValue>
                {
                    ["PLOT"] = AttributeValue.FromText("00742"),
                    ["RARE"] = AttributeValue.FromText("only here"),
                }),
                Sparse("narrow", new Dictionary<string, AttributeValue>
                {
                    ["PLOT"] = AttributeValue.FromText("00743"),
                    ["RARE"] = AttributeValue.Null(AttributeDataType.Text),
                })));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        JsonElement features = document.RootElement.GetProperty("features");

        features[0].GetProperty("properties").TryGetProperty("RARE", out _).Should().BeTrue();
        features[1].GetProperty("properties").TryGetProperty("RARE", out _).Should().BeFalse();
    }

    [Fact]
    public async Task GeometryAndIdentityAreUntouched()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("intact.geojson");

        await Exporter(true).WriteAsync(
            FeatureFactory.Request(path),
            Yield(Sparse("f1", new Dictionary<string, AttributeValue>
            {
                ["EMPTY"] = AttributeValue.Null(AttributeDataType.Text),
            })));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        JsonElement feature = document.RootElement.GetProperty("features")[0];

        feature.GetProperty("id").GetString().Should().Be("f1");
        feature.GetProperty("type").GetString().Should().Be("Feature");
        feature.GetProperty("geometry").GetProperty("type").GetString().Should().Be("Point");
        feature.GetProperty("geometry").GetProperty("coordinates")[0].GetDouble().Should().Be(1d);
    }

    [Fact]
    public void OmissionIsTheDefault()
    {
        new GisOptions().Export.OmitNullGeoJsonProperties.Should().BeTrue();
    }
}
