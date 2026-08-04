using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Profiles;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Geometry;

/// <summary>Default <see cref="IGeometryMapper"/>.</summary>
public sealed class GeometryMapper : IGeometryMapper
{
    /// <inheritdoc />
    public Result<NtsGeometry> Map(NtsGeometry geometry, GeometryRules rules)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(rules);

        NtsGeometry working = geometry;

        if (rules.ClosedLinesToPolygons && working is LineString { IsClosed: true, NumPoints: >= 4 } closed)
        {
            // A closed line is an area to a GIS and a line to CAD. Profiles targeting parcel data
            // ask for the area reading; profiles targeting utilities do not.
            working = working.Factory.CreatePolygon(working.Factory.CreateLinearRing(closed.Coordinates));
        }

        if (rules.ExteriorRingOrientation != RingOrientationRule.Preserve)
        {
            working = RingOrientationNormaliser.Normalise(working, rules.ExteriorRingOrientation);
        }

        if (rules.PromoteToMulti)
        {
            working = Promote(working);
        }

        return working.IsEmpty
            ? Result.Failure<NtsGeometry>(new Error("Gis.EmptyAfterMapping", "Mapping produced an empty geometry."))
            : Result.Success(working);
    }

    /// <inheritdoc />
    public GeometryKind Classify(NtsGeometry? geometry) => geometry switch
    {
        null => GeometryKind.Unknown,
        Point or MultiPoint => GeometryKind.Point,
        LineString or MultiLineString or LinearRing => GeometryKind.Line,
        Polygon or MultiPolygon => GeometryKind.Polygon,
        GeometryCollection collection when collection.NumGeometries > 0 => Classify(collection.GetGeometryN(0)),
        _ => GeometryKind.Unknown,
    };

    /// <inheritdoc />
    public IEnumerable<NtsGeometry> Explode(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        // Multi-part geometries are left whole: they are a single feature with several parts, and
        // splitting them would multiply the attribute row. Only a heterogeneous collection, which
        // no mainstream format can store, is split.
        if (geometry is not GeometryCollection collection ||
            geometry is MultiPoint or MultiLineString or MultiPolygon)
        {
            yield return geometry;
            yield break;
        }

        foreach (NtsGeometry part in collection.Geometries)
        {
            foreach (NtsGeometry nested in Explode(part))
            {
                yield return nested;
            }
        }
    }

    /// <summary>Wraps a single geometry in its multi-part equivalent.</summary>
    private static NtsGeometry Promote(NtsGeometry geometry)
    {
        GeometryFactory factory = geometry.Factory;

        return geometry switch
        {
            Point point => factory.CreateMultiPoint([point]),
            LineString line => factory.CreateMultiLineString([line]),
            Polygon polygon => factory.CreateMultiPolygon([polygon]),
            _ => geometry,
        };
    }
}
