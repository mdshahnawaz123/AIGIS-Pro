using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.QaQc.Abstractions;
using AiGisConverter.QaQc.Options;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.QaQc.Tests.TestSupport;

/// <summary>Builds datasets and contexts for the rule tests.</summary>
internal static class QaQcTestFactory
{
    public static readonly GeometryFactory Geometry = new();

    public static Polygon Square(double x, double y, double size) =>
        Geometry.CreatePolygon(Geometry.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + size, y),
            new Coordinate(x + size, y + size),
            new Coordinate(x, y + size),
            new Coordinate(x, y),
        ]));

    public static Polygon Rectangle(double x, double y, double width, double height) =>
        Geometry.CreatePolygon(Geometry.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + width, y),
            new Coordinate(x + width, y + height),
            new Coordinate(x, y + height),
            new Coordinate(x, y),
        ]));

    public static LineString Line(double x1, double y1, double x2, double y2) =>
        Geometry.CreateLineString([new Coordinate(x1, y1), new Coordinate(x2, y2)]);

    public static GisFeature Feature(
        string id,
        NtsGeometry? geometry,
        GeometryKind kind = GeometryKind.Polygon,
        params (string Field, string Value)[] attributes) =>
        new(id,
            FeatureClass.Create("PARCEL", kind),
            geometry,
            attributes.ToDictionary(
                static a => a.Field,
                static a => AttributeValue.FromText(a.Value),
                StringComparer.OrdinalIgnoreCase),
            LayerName.Create("C-PARCEL"),
            id);

    public static GisAttributeSchema Schema(params string[] fields) =>
        new([.. fields.Select(f => FieldDefinition.Create(f, AttributeDataType.Text, 254))]);

    public static GisDataset Dataset(
        IEnumerable<GisFeature> features,
        GeometryKind kind = GeometryKind.Polygon,
        CoordinateSystem? crs = null,
        GisAttributeSchema? schema = null,
        string name = "PARCEL") =>
        new(FeatureClass.Create(name, kind),
            crs ?? CoordinateSystem.Create("EPSG", 27700),
            schema ?? GisAttributeSchema.Empty,
            features);

    public static ValidationContext Context(GisDataset dataset, Action<QaQcOptions>? configure = null)
    {
        QaQcOptions options = new();
        configure?.Invoke(options);

        return new ValidationContext(dataset, options);
    }

    public static IOptionsMonitor<QaQcOptions> Monitor(Action<QaQcOptions>? configure = null)
    {
        QaQcOptions options = new();
        configure?.Invoke(options);

        IOptionsMonitor<QaQcOptions> monitor = Substitute.For<IOptionsMonitor<QaQcOptions>>();
        monitor.CurrentValue.Returns(options);

        return monitor;
    }
}
