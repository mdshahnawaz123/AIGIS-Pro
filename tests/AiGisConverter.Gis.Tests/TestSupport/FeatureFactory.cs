using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Profiles;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.TestSupport;

/// <summary>Builds features, schemas and requests for exporter tests.</summary>
internal static class FeatureFactory
{
    public static readonly GeometryFactory Geometry = new();

    public static GisAttributeSchema Schema() => new(
    [
        FieldDefinition.Create("PLOT", AttributeDataType.Text, 10),
        FieldDefinition.Create("AREA", AttributeDataType.Double),
        FieldDefinition.Create("ACTIVE", AttributeDataType.Boolean),
    ]);

    public static GisFeature Polygon(string id, double x, double y, double size = 10d, string plot = "00742")
    {
        Polygon polygon = Geometry.CreatePolygon(Geometry.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + size, y),
            new Coordinate(x + size, y + size),
            new Coordinate(x, y + size),
            new Coordinate(x, y),
        ]));

        return new GisFeature(
            id,
            FeatureClass.Create("PARCEL", GeometryKind.Polygon),
            polygon,
            new Dictionary<string, AttributeValue>
            {
                ["PLOT"] = AttributeValue.FromText(plot),
                ["AREA"] = AttributeValue.FromDouble(size * size),
                ["ACTIVE"] = AttributeValue.FromBoolean(true),
            },
            LayerName.Create("C-PARCEL"),
            id);
    }

    public static GisFeature Point(string id, double x, double y) =>
        new(id,
            FeatureClass.Create("NODE", GeometryKind.Point),
            Geometry.CreatePoint(new Coordinate(x, y)),
            new Dictionary<string, AttributeValue> { ["PLOT"] = AttributeValue.FromText(id) },
            LayerName.Create("C-NODE"),
            id);

    public static ExportRequest Request(string path, CoordinateSystem? crs = null) =>
        new(path,
            FeatureClass.Create("PARCEL", GeometryKind.Polygon),
            Schema(),
            crs ?? CoordinateSystem.Wgs84,
            new GisConversionContext(new ConversionProfile { Id = "test" }, CoordinateSystem.Wgs84, crs ?? CoordinateSystem.Wgs84));

    public static async IAsyncEnumerable<GisFeature> Stream(int count, double spacing = 20d)
    {
        for (int i = 0; i < count; i++)
        {
            yield return Polygon($"f{i}", i * spacing, 0d);

            if (i % 1000 == 0)
            {
                await Task.Yield();
            }
        }
    }
}
