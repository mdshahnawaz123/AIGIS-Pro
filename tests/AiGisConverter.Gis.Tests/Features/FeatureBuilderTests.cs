using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Features;
using AiGisConverter.Gis.Profiles;
using AiGisConverter.Gis.Tests.TestSupport;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Tests.Features;

/// <summary>
/// H1 regression: the builder must never emit a feature carrying a null geometry.
/// </summary>
/// <remarks>
/// The defect these tests lock down produced features with attributes (Handle, Layer) but
/// <c>Geometry = null</c> — thousands of them — because the builder emitted a feature whenever the
/// geometry was null or empty and <c>DropIrreparableGeometry</c> was left at its default of false.
/// The contract now: skip when configured to drop, otherwise substitute a valid empty geometry, so
/// no exported feature is ever null.
/// </remarks>
public sealed class FeatureBuilderTests
{
    private static readonly GeometryFactory Factory = new();

    private static FeatureBuilder Build(bool dropIrreparable)
    {
        IGeometryMapper mapper = Substitute.For<IGeometryMapper>();
        mapper.Map(Arg.Any<NtsGeometry>(), Arg.Any<GeometryRules>())
            .Returns(call => Result.Success(call.Arg<NtsGeometry>()));

        IGeometryValidator validator = Substitute.For<IGeometryValidator>();
        validator.Validate(Arg.Any<NtsGeometry?>(), Arg.Any<string>(), Arg.Any<QualityRules>())
            .Returns(System.Array.Empty<ValidationIssue>());

        IGeometryRepairer repairer = Substitute.For<IGeometryRepairer>();
        repairer.Repair(Arg.Any<NtsGeometry>())
            .Returns(call => GeometryRepairResult.Unchanged(call.Arg<NtsGeometry>()));

        IGeometrySimplifier simplifier = Substitute.For<IGeometrySimplifier>();
        simplifier.Simplify(Arg.Any<NtsGeometry>(), Arg.Any<double>())
            .Returns(call => call.Arg<NtsGeometry>());

        IAttributeMapper attributes = Substitute.For<IAttributeMapper>();
        attributes.Map(Arg.Any<SourceElement>(), Arg.Any<GisAttributeSchema>(), Arg.Any<ConversionProfile>())
            .Returns(new Dictionary<string, AttributeValue>
            {
                ["Handle"] = AttributeValue.FromText("2A"),
                ["Layer"] = AttributeValue.FromText("WALLS"),
            });

        Domain.Abstractions.Services.ICoordinateTransformer transformer =
            Substitute.For<Domain.Abstractions.Services.ICoordinateTransformer>();

        return new FeatureBuilder(
            mapper,
            validator,
            repairer,
            simplifier,
            attributes,
            transformer,
            GisOptionsFactory.Monitor(o => o.Geometry.DropIrreparableGeometry = dropIrreparable),
            Substitute.For<ILogger<FeatureBuilder>>());
    }

    private static SourceLayer LayerWith(params SourceElement[] elements)
    {
        SourceLayer layer = new("WALLS");

        foreach (SourceElement element in elements)
        {
            layer.AddElement(element);
        }

        return layer;
    }

    private static SourceElement Element(string id, GeometryKind kind, NtsGeometry? geometry) =>
        new(id, kind) { Geometry = geometry };

    private static GisConversionContext Context() =>
        new(new ConversionProfile { Id = "test" }, CoordinateSystem.Wgs84, CoordinateSystem.Wgs84);

    private static async Task<List<GisFeature>> Collect(FeatureBuilder builder, SourceLayer layer, GeometryKind kind)
    {
        GisConversionContext context = Context();
        FeatureClass featureClass = FeatureClass.Create("WALL", kind);
        List<GisFeature> features = [];

        await foreach (GisFeature feature in builder.BuildAsync(layer, featureClass, FeatureFactory.Schema(), context))
        {
            features.Add(feature);
        }

        return features;
    }

    private static Polygon ValidSquare(double x, double y, double size = 10d) =>
        Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + size, y),
            new Coordinate(x + size, y + size),
            new Coordinate(x, y + size),
            new Coordinate(x, y),
        ]));

    [Fact]
    public async Task NullGeometry_WithDropDisabled_EmitsFeatureWithValidEmptyGeometry()
    {
        FeatureBuilder builder = Build(dropIrreparable: false);
        SourceLayer layer = LayerWith(Element("1", GeometryKind.Polygon, geometry: null));

        List<GisFeature> features = await Collect(builder, layer, GeometryKind.Polygon);

        features.Should().ContainSingle();
        features[0].Geometry.Should().NotBeNull("a feature must never carry a null geometry");
        features[0].Geometry!.IsEmpty.Should().BeTrue();
        features[0].Geometry!.IsValid.Should().BeTrue();
        features[0].Geometry.Should().BeOfType<Polygon>("the empty geometry matches the feature class family");
    }

    [Fact]
    public async Task NullGeometry_WithDropEnabled_SkipsTheFeature()
    {
        FeatureBuilder builder = Build(dropIrreparable: true);
        SourceLayer layer = LayerWith(Element("1", GeometryKind.Polygon, geometry: null));

        List<GisFeature> features = await Collect(builder, layer, GeometryKind.Polygon);

        features.Should().BeEmpty("DropIrreparableGeometry keeps its meaning: discard rather than export");
    }

    [Fact]
    public async Task EmptyGeometryOnEntry_IsTreatedAsUnusable()
    {
        FeatureBuilder builder = Build(dropIrreparable: false);
        SourceLayer layer = LayerWith(Element("1", GeometryKind.Line, Factory.CreateLineString()));

        List<GisFeature> features = await Collect(builder, layer, GeometryKind.Line);

        features.Should().ContainSingle();
        features[0].Geometry.Should().BeOfType<LineString>();
        features[0].Geometry!.IsEmpty.Should().BeTrue();
    }

    [Fact]
    public async Task ValidGeometry_IsPreservedUnchanged()
    {
        FeatureBuilder builder = Build(dropIrreparable: false);
        Polygon square = ValidSquare(0, 0);
        SourceLayer layer = LayerWith(Element("1", GeometryKind.Polygon, square));

        List<GisFeature> features = await Collect(builder, layer, GeometryKind.Polygon);

        features.Should().ContainSingle();
        features[0].Geometry.Should().NotBeNull();
        features[0].Geometry!.IsEmpty.Should().BeFalse();
        features[0].Geometry!.Area.Should().BeApproximately(100d, 1e-9d);
    }

    [Fact]
    public async Task MixedLayer_NoExportedFeatureHasNullGeometry()
    {
        FeatureBuilder builder = Build(dropIrreparable: false);
        SourceLayer layer = LayerWith(
            Element("valid", GeometryKind.Polygon, ValidSquare(0, 0)),
            Element("null", GeometryKind.Polygon, geometry: null),
            Element("empty", GeometryKind.Polygon, Factory.CreatePolygon()));

        List<GisFeature> features = await Collect(builder, layer, GeometryKind.Polygon);

        features.Should().HaveCount(3, "keep mode preserves the feature count");
        features.Should().OnlyContain(f => f.Geometry != null, "the H1 guarantee");
    }
}
