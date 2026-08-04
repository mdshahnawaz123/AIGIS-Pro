// ---------------------------------------------------------------------------------------------
// netDxf BOUNDARY FILE (1 of 2).
//
// Every reference to the netDxf library in this assembly lives in this file and in DxfProvider.cs.
// Nothing here performs geometric computation: it pulls numbers out of netDxf objects and hands
// them to AiGisConverter.Cad.Geometry, which is vendor-free and unit-tested.
//
// The consequence is that a netDxf major-version rename touches two files and no maths.
// ---------------------------------------------------------------------------------------------

using AiGisConverter.Cad.Geometry;
using AiGisConverter.Cad.Options;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using netDxf;
using netDxf.Entities;
using NetDxfPoint = netDxf.Entities.Point;
using NetDxfAttribute = netDxf.Entities.Attribute;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using NtsCoordinate = NetTopologySuite.Geometries.Coordinate;
using NtsCoordinateZ = NetTopologySuite.Geometries.CoordinateZ;

namespace AiGisConverter.Cad.Providers.Dxf;

/// <summary>
/// Converts netDxf entities into the domain's <see cref="SourceElement"/>.
/// </summary>
internal sealed class NetDxfEntityConverter
{
    private static readonly NtsGeometryFactory Factory = new();

    private readonly HashSet<string> _warnedTypes = new(StringComparer.Ordinal);
    private readonly CadOptions _options;
    private readonly SourceDocument _document;

    /// <summary>Initializes a new instance of the <see cref="NetDxfEntityConverter"/> class.</summary>
    /// <param name="options">The CAD reading settings.</param>
    /// <param name="document">The document being built, used to record warnings.</param>
    public NetDxfEntityConverter(CadOptions options, SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(document);

        _options = options;
        _document = document;
    }

    /// <summary>
    /// Converts one entity, expanding block references when configured to.
    /// </summary>
    /// <param name="entity">The netDxf entity.</param>
    /// <param name="transform">The transform in force, from any enclosing block.</param>
    /// <param name="depth">Current block nesting depth.</param>
    /// <returns>Zero or more elements. A block may yield many; an ignored entity yields none.</returns>
    public IEnumerable<ConvertedElement> Convert(EntityObject entity, BlockTransform transform, int depth)
    {
        ArgumentNullException.ThrowIfNull(entity);

        switch (entity)
        {
            case Line line:
                yield return Element(entity, GeometryKind.Line, "LINE", transform.Apply(
                    Factory.CreateLineString([ToCoordinate(line.StartPoint), ToCoordinate(line.EndPoint)])));
                break;

            case Polyline2D polyline:
                foreach (ConvertedElement element in ConvertPolyline2D(polyline, transform))
                {
                    yield return element;
                }

                break;

            case Polyline3D polyline:
                yield return ConvertPolyline3D(polyline, transform);
                break;

            case Arc arc:
                yield return Element(entity, GeometryKind.Line, "ARC", transform.Apply(
                    Factory.CreateLineString(CurveTessellator.Arc(
                        arc.Center.X,
                        arc.Center.Y,
                        arc.Radius,
                        Radians(arc.StartAngle),
                        CurveTessellation.CounterClockwiseSweep(Radians(arc.StartAngle), Radians(arc.EndAngle)),
                        _options.Tessellation,
                        arc.Center.Z))));
                break;

            case Circle circle:
                yield return ConvertCircle(circle, transform);
                break;

            case Ellipse ellipse:
                yield return ConvertEllipse(ellipse, transform);
                break;

            case NetDxfPoint point:
                yield return Element(entity, GeometryKind.Point, "POINT", transform.Apply(
                    Factory.CreatePoint(ToCoordinate(point.Position))));
                break;

            case Hatch hatch when _options.ReadHatches:
                ConvertedElement? converted = ConvertHatch(hatch, transform);

                if (converted is not null)
                {
                    yield return converted;
                }

                break;

            case Text text when _options.ReadText:
                yield return ConvertText(entity, text.Value, text.Position, text.Height, text.Rotation, "TEXT", transform);
                break;

            case MText mText when _options.ReadText:
                yield return ConvertText(
                    entity,
                    SafePlainText(mText),
                    mText.Position,
                    mText.Height,
                    mText.Rotation,
                    "MTEXT",
                    transform);
                break;

            case Insert insert:
                foreach (ConvertedElement element in ConvertInsert(insert, transform, depth))
                {
                    yield return element;
                }

                break;

            case Spline spline:
                yield return ConvertSpline(spline, transform);
                break;

            // Deliberately switched off in configuration: skipped without comment, because the
            // operator already knows and a warning per entity would bury the real findings.
            case Hatch:
            case Text:
            case MText:
                break;

            case Dimension:
                if (_options.ReadDimensions)
                {
                    WarnOnce(entity, "Dimension geometry is not yet converted");
                }

                break;

            default:
                // Genuinely unhandled types are reported once per type, so a drawing that loses
                // content says so rather than quietly arriving incomplete - and a file with fifty
                // thousand unsupported entities produces one line, not fifty thousand.
                WarnOnce(entity, "is not supported");
                break;
        }
    }

    private IEnumerable<ConvertedElement> ConvertPolyline2D(Polyline2D polyline, BlockTransform transform)
    {
        PolylineVertex[] vertices = [.. polyline.Vertexes.Select(static v =>
            new PolylineVertex(v.Position.X, v.Position.Y, v.Bulge))];

        NtsCoordinate[] coordinates = CurveTessellator.Polyline(
            vertices,
            polyline.IsClosed,
            _options.Tessellation,
            polyline.Elevation);

        if (coordinates.Length < 2)
        {
            yield break;
        }

        // A closed polyline is an area to a GIS and a line to CAD. Emitting it as a polygon is the
        // reading that survives export; a closed LineString is legal but almost never wanted.
        if (polyline.IsClosed && coordinates.Length >= 4)
        {
            if (PolygonAssembler.TryCloseRing(coordinates, out NetTopologySuite.Geometries.LinearRing? ring))
            {
                yield return Element(polyline, GeometryKind.Polygon, "LWPOLYLINE", transform.Apply(
                    Factory.CreatePolygon(ring!)));
                yield break;
            }
        }

        yield return Element(polyline, GeometryKind.Line, "LWPOLYLINE", transform.Apply(
            Factory.CreateLineString(coordinates)));
    }

    private ConvertedElement ConvertPolyline3D(Polyline3D polyline, BlockTransform transform)
    {
        NtsCoordinate[] coordinates = [.. polyline.Vertexes.Select(ToCoordinate)];

        if (polyline.IsClosed && coordinates.Length >= 3)
        {
            coordinates = [.. coordinates, coordinates[0].Copy()];
        }

        return coordinates.Length >= 2
            ? Element(polyline, GeometryKind.Line, "POLYLINE", transform.Apply(Factory.CreateLineString(coordinates)))
            : Element(polyline, GeometryKind.Unknown, "POLYLINE", null);
    }

    private ConvertedElement ConvertCircle(Circle circle, BlockTransform transform)
    {
        NtsCoordinate[] ring = CurveTessellator.Circle(
            circle.Center.X,
            circle.Center.Y,
            circle.Radius,
            _options.Tessellation,
            circle.Center.Z);

        ConvertedElement element = PolygonAssembler.TryCloseRing(ring, out NetTopologySuite.Geometries.LinearRing? closed)
            ? Element(circle, GeometryKind.Polygon, "CIRCLE", transform.Apply(Factory.CreatePolygon(closed!)))
            : Element(circle, GeometryKind.Unknown, "CIRCLE", null);

        element.Attributes["Radius"] = circle.Radius;

        return element;
    }

    private ConvertedElement ConvertEllipse(Ellipse ellipse, BlockTransform transform)
    {
        double start = Radians(ellipse.StartAngle);
        double sweep = CurveTessellation.CounterClockwiseSweep(start, Radians(ellipse.EndAngle));

        NtsCoordinate[] points = CurveTessellator.EllipticalArc(
            ellipse.Center.X,
            ellipse.Center.Y,
            ellipse.MajorAxis / 2d,
            ellipse.MinorAxis / 2d,
            Radians(ellipse.Rotation),
            start,
            sweep,
            _options.Tessellation,
            ellipse.Center.Z);

        bool isFull = Math.Abs(sweep - (2d * Math.PI)) < 1e-9d;

        if (isFull && PolygonAssembler.TryCloseRing(points, out NetTopologySuite.Geometries.LinearRing? ring))
        {
            return Element(ellipse, GeometryKind.Polygon, "ELLIPSE", transform.Apply(Factory.CreatePolygon(ring!)));
        }

        return points.Length >= 2
            ? Element(ellipse, GeometryKind.Line, "ELLIPSE", transform.Apply(Factory.CreateLineString(points)))
            : Element(ellipse, GeometryKind.Unknown, "ELLIPSE", null);
    }

    private ConvertedElement ConvertSpline(Spline spline, BlockTransform transform)
    {
        List<Vector3> sampled;

        try
        {
            sampled = spline.PolygonalVertexes(_options.Tessellation.SegmentsPerSplineSpan);
        }
        catch (ArithmeticException)
        {
            _document.AddWarning($"Spline '{spline.Handle}' could not be evaluated and was skipped.");
            return Element(spline, GeometryKind.Unknown, "SPLINE", null);
        }

        NtsCoordinate[] coordinates = [.. sampled.Select(ToCoordinate)];

        return coordinates.Length >= 2
            ? Element(spline, GeometryKind.Line, "SPLINE", transform.Apply(Factory.CreateLineString(coordinates)))
            : Element(spline, GeometryKind.Unknown, "SPLINE", null);
    }

    private ConvertedElement? ConvertHatch(Hatch hatch, BlockTransform transform)
    {
        List<NtsCoordinate[]> rings = [];

        foreach (HatchBoundaryPath path in hatch.BoundaryPaths)
        {
            List<NtsCoordinate> ring = [];

            foreach (EntityObject boundaryEntity in path.Entities)
            {
                ring.AddRange(BoundaryCoordinates(boundaryEntity));
            }

            if (ring.Count >= 3)
            {
                rings.Add([.. ring]);
            }
        }

        if (rings.Count == 0)
        {
            _document.AddWarning($"Hatch '{hatch.Handle}' had no usable boundary and was skipped.");
            return null;
        }

        NtsGeometry? polygon = PolygonAssembler.Assemble(rings);

        if (polygon is null)
        {
            _document.AddWarning($"Hatch '{hatch.Handle}' boundary could not be closed and was skipped.");
            return null;
        }

        ConvertedElement element = Element(hatch, GeometryKind.Polygon, "HATCH", transform.Apply(polygon));
        element.Attributes["HatchPattern"] = hatch.Pattern?.Name;

        return element;
    }

    /// <summary>Flattens one hatch boundary sub-entity into coordinates.</summary>
    private IEnumerable<NtsCoordinate> BoundaryCoordinates(EntityObject entity) => entity switch
    {
        Line line => [ToCoordinate(line.StartPoint), ToCoordinate(line.EndPoint)],

        Arc arc => CurveTessellator.Arc(
            arc.Center.X,
            arc.Center.Y,
            arc.Radius,
            Radians(arc.StartAngle),
            CurveTessellation.CounterClockwiseSweep(Radians(arc.StartAngle), Radians(arc.EndAngle)),
            _options.Tessellation,
            arc.Center.Z),

        Circle circle => CurveTessellator.Circle(
            circle.Center.X,
            circle.Center.Y,
            circle.Radius,
            _options.Tessellation,
            circle.Center.Z),

        Polyline2D polyline => CurveTessellator.Polyline(
            [.. polyline.Vertexes.Select(static v => new PolylineVertex(v.Position.X, v.Position.Y, v.Bulge))],
            polyline.IsClosed,
            _options.Tessellation,
            polyline.Elevation),

        Ellipse ellipse => CurveTessellator.EllipticalArc(
            ellipse.Center.X,
            ellipse.Center.Y,
            ellipse.MajorAxis / 2d,
            ellipse.MinorAxis / 2d,
            Radians(ellipse.Rotation),
            Radians(ellipse.StartAngle),
            CurveTessellation.CounterClockwiseSweep(Radians(ellipse.StartAngle), Radians(ellipse.EndAngle)),
            _options.Tessellation,
            ellipse.Center.Z),

        _ => [],
    };

    private ConvertedElement ConvertText(
        EntityObject entity,
        string? value,
        Vector3 position,
        double height,
        double rotation,
        string nativeType,
        BlockTransform transform)
    {
        ConvertedElement element = Element(
            entity,
            GeometryKind.Annotation,
            nativeType,
            transform.Apply(Factory.CreatePoint(ToCoordinate(position))));

        element.Text = value;
        element.Attributes["Text"] = value;
        element.Attributes["TextHeight"] = height;
        element.Attributes["TextRotation"] = rotation;

        return element;
    }

    private IEnumerable<ConvertedElement> ConvertInsert(Insert insert, BlockTransform transform, int depth)
    {
        BlockTransform local = transform.Compose(new BlockTransform(
            insert.Position.X,
            insert.Position.Y,
            insert.Position.Z,
            insert.Scale.X,
            insert.Scale.Y,
            insert.Scale.Z,
            Radians(insert.Rotation)));

        Dictionary<string, object?> blockAttributes = ReadBlockAttributes(insert);

        if (!_options.ExplodeBlocks || depth >= _options.MaxBlockNestingDepth)
        {
            if (depth >= _options.MaxBlockNestingDepth && _options.ExplodeBlocks)
            {
                _document.AddWarning(
                    $"Block '{insert.Block?.Name}' exceeded the nesting limit of " +
                    $"{_options.MaxBlockNestingDepth} and was emitted as a point.");
            }

            ConvertedElement point = Element(
                insert,
                GeometryKind.Point,
                "INSERT",
                transform.Apply(Factory.CreatePoint(ToCoordinate(insert.Position))));

            point.Attributes["BlockName"] = insert.Block?.Name;
            point.Attributes["BlockRotation"] = insert.Rotation;
            point.Attributes["BlockScaleX"] = insert.Scale.X;
            point.Attributes["BlockScaleY"] = insert.Scale.Y;

            foreach (KeyValuePair<string, object?> attribute in blockAttributes)
            {
                point.Attributes[attribute.Key] = attribute.Value;
            }

            yield return point;
            yield break;
        }

        if (insert.Block is null)
        {
            yield break;
        }

        foreach (EntityObject child in insert.Block.Entities)
        {
            foreach (ConvertedElement element in Convert(child, local, depth + 1))
            {
                element.Attributes["BlockName"] = insert.Block.Name;

                foreach (KeyValuePair<string, object?> attribute in blockAttributes)
                {
                    element.Attributes[attribute.Key] = attribute.Value;
                }

                yield return element;
            }
        }
    }

    /// <summary>Reads the visible attribute values attached to a block reference.</summary>
    /// <remarks>
    /// Block attributes are where drawings actually keep asset data &#8212; pipe diameters, manhole
    /// references, tree species. Losing them turns a converted drawing into geometry with no
    /// information attached, so they are read by default.
    /// </remarks>
    private Dictionary<string, object?> ReadBlockAttributes(Insert insert)
    {
        Dictionary<string, object?> attributes = new(StringComparer.OrdinalIgnoreCase);

        if (!_options.ReadBlockAttributes || insert.Attributes is null)
        {
            return attributes;
        }

        foreach (NetDxfAttribute attribute in insert.Attributes)
        {
            string? tag = attribute.Tag;

            if (!string.IsNullOrWhiteSpace(tag))
            {
                attributes[tag.Trim()] = attribute.Value;
            }
        }

        return attributes;
    }

    /// <summary>Records an unsupported-entity warning at most once per entity type.</summary>
    private void WarnOnce(EntityObject entity, string reason)
    {
        string typeName = entity.GetType().Name;

        if (_warnedTypes.Add(typeName))
        {
            _document.AddWarning($"Entity type '{typeName}' {reason} and was skipped.");
        }
    }

    private static ConvertedElement Element(
        EntityObject entity,
        GeometryKind kind,
        string nativeType,
        NtsGeometry? geometry)
    {
        ConvertedElement element = new(
            string.IsNullOrWhiteSpace(entity.Handle) ? Guid.NewGuid().ToString("N") : entity.Handle,
            kind,
            entity.Layer?.Name ?? "0")
        {
            NativeType = nativeType,
            Geometry = geometry,
        };

        element.Attributes["Handle"] = entity.Handle;
        element.Attributes["Layer"] = entity.Layer?.Name;
        element.Attributes["Color"] = entity.Color?.Index.ToString();
        element.Attributes["Linetype"] = entity.Linetype?.Name;
        element.Attributes["Lineweight"] = entity.Lineweight.ToString();

        if (geometry is not null)
        {
            if (geometry is NetTopologySuite.Geometries.Polygon || geometry is NetTopologySuite.Geometries.MultiPolygon)
            {
                element.Attributes["Area"] = geometry.Area;
                element.Attributes["Closed"] = true;
            }
            else if (geometry is NetTopologySuite.Geometries.LineString ls)
            {
                element.Attributes["Length"] = geometry.Length;
                element.Attributes["Closed"] = ls.IsClosed;
            }
            else
            {
                element.Attributes["Closed"] = false;
            }
        }

        if (entity.XData != null && entity.XData.Count > 0)
        {
            element.Attributes["XData"] = string.Join(",", entity.XData.Values.Select(x => x.ApplicationRegistry.Name));
        }

        return element;
    }

    private static string? SafePlainText(MText mText)
    {
        try
        {
            return mText.PlainText();
        }
        catch (FormatException)
        {
            // Malformed MText formatting codes are common in files from older exporters.
            return mText.Value;
        }
    }

    private static NtsCoordinate ToCoordinate(Vector3 vector) =>
        double.IsNaN(vector.Z) ? new NtsCoordinate(vector.X, vector.Y) : new NtsCoordinateZ(vector.X, vector.Y, vector.Z);

    private static double Radians(double degrees) => degrees * Math.PI / 180d;
}

/// <summary>
/// A converted entity, still carrying the layer it came from so the reader can file it correctly.
/// </summary>
internal sealed class ConvertedElement
{
    public ConvertedElement(string id, GeometryKind kind, string layerName)
    {
        Id = id;
        Kind = kind;
        LayerName = layerName;
    }

    public string Id { get; }

    public GeometryKind Kind { get; }

    public string LayerName { get; }

    public string? NativeType { get; init; }

    public NtsGeometry? Geometry { get; init; }

    public string? Text { get; set; }

    public Dictionary<string, object?> Attributes { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Projects onto the domain model.</summary>
    /// <returns>The domain element.</returns>
    public SourceElement ToSourceElement()
    {
        SourceElement element = new(Id, Kind)
        {
            Geometry = Geometry,
            NativeType = NativeType,
            Text = Text,
        };

        element.SetAttributes(Attributes);

        return element;
    }
}
