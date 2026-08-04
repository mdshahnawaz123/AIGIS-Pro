using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;

namespace AiGisConverter.Plugins.LandXml;

/// <summary>
/// Reads LandXML survey and design data into the format-neutral source model.
/// </summary>
/// <remarks>
/// <para>
/// LandXML is plain XML, so this reader needs no SDK, no host application and no native payload —
/// it is the one civil format that works on any machine the converter runs on.
/// </para>
/// <para>
/// Each LandXML collection becomes its own layer (<c>CgPoints</c>, <c>Parcels</c>,
/// <c>Alignments</c>), because those are the divisions a surveyor already thinks in and the ones
/// the mapping rules will want to target. Survey metadata — point codes, parcel areas, alignment
/// stationing — is carried onto the elements as attributes rather than discarded, so it survives
/// into the GIS output and into the semantic layer later.
/// </para>
/// </remarks>
internal sealed class LandXmlReader : IDataSourceReader
{
    private static readonly NtsGeometryFactory Factory = new();

    private readonly IPluginContext _context;

    /// <summary>Initializes a new instance of the <see cref="LandXmlReader"/> class.</summary>
    /// <param name="context">The plugin context.</param>
    public LandXmlReader(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>Gets a value indicating whether the format backend is available.</summary>
    /// <remarks>Always true: the reader depends on nothing beyond the .NET XML stack.</remarks>
    public static bool IsBackendAvailable => true;

    /// <inheritdoc />
    public string FormatKey => "landxml";

    /// <inheritdoc />
    public string DisplayName => "LandXML";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = [".landxml", ".xml"];

    /// <inheritdoc />
    public bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        if (!SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase)
            || !File.Exists(reference.Location))
        {
            return false;
        }

        // ".xml" alone claims far too much, so the root element decides. Reading only the root
        // keeps this cheap enough to run against every candidate file in a folder.
        try
        {
            using XmlReader reader = XmlReader.Create(
                reference.Location,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            return reader.MoveToContent() == XmlNodeType.Element
                && string.Equals(reader.LocalName, "LandXML", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return Task.Run(() => Read(reference, progress, cancellationToken), cancellationToken);
    }

    private Result<SourceDocument> Read(
        SourceReference reference,
        IProgress<ReadProgress>? progress,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(reference.Location))
        {
            return Result.Failure<SourceDocument>(new Error(
                "LandXml.FileNotFound",
                $"'{reference.Location}' does not exist."));
        }

        progress?.Report(new ReadProgress(0d, "Opening LandXML..."));

        XDocument document;

        try
        {
            using XmlReader xmlReader = XmlReader.Create(
                reference.Location,
                new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });

            document = XDocument.Load(xmlReader, LoadOptions.None);
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            return Result.Failure<SourceDocument>(new Error(
                "LandXml.MalformedDocument",
                $"'{Path.GetFileName(reference.Location)}' could not be parsed as LandXML: {ex.Message}"));
        }

        if (document.Root is null
            || !string.Equals(document.Root.Name.LocalName, "LandXML", StringComparison.OrdinalIgnoreCase))
        {
            return Result.Failure<SourceDocument>(new Error(
                "LandXml.NotLandXml",
                $"'{Path.GetFileName(reference.Location)}' is not a LandXML document."));
        }

        SourceDocument source = new(reference, FormatKey);
        XNamespace ns = document.Root.Name.Namespace;

        ApplyHeader(source, document.Root, ns);

        progress?.Report(new ReadProgress(0.2d, "Reading survey points..."));
        int points = ReadCogoPoints(source, document.Root, ns, cancellationToken);

        progress?.Report(new ReadProgress(0.5d, "Reading parcels..."));
        int parcels = ReadParcels(source, document.Root, ns, cancellationToken);

        progress?.Report(new ReadProgress(0.6d, "Reading alignments..."));
        int alignments = ReadAlignments(source, document.Root, ns, cancellationToken);

        progress?.Report(new ReadProgress(0.7d, "Reading surfaces..."));
        int surfaces = ReadSurfaces(source, document.Root, ns, cancellationToken);

        progress?.Report(new ReadProgress(0.85d, "Reading plan features..."));
        int planFeatures = ReadPlanFeatures(source, document.Root, ns, cancellationToken);

        progress?.Report(new ReadProgress(0.95d, "Reading pipe networks..."));
        int network = ReadPipeNetworks(source, document.Root, ns, cancellationToken);

        int total = points + parcels + alignments + surfaces + planFeatures + network;

        if (total == 0)
        {
            source.AddWarning("The document contains no readable LandXML geometry.");
        }

        progress?.Report(new ReadProgress(1d, $"Read {total:N0} elements."));

        _context.Logger.LogInformation(
            "Read {Total} LandXML elements from {File} ({Points} points, {Parcels} parcels, "
            + "{Alignments} alignments, {Surfaces} surface elements, {PlanFeatures} plan features, "
            + "{Network} pipe-network elements).",
            total,
            Path.GetFileName(reference.Location),
            points,
            parcels,
            alignments,
            surfaces,
            planFeatures,
            network);

        return Result.Success(source);
    }

    /// <summary>Records units and any declared coordinate system.</summary>
    private static void ApplyHeader(SourceDocument document, XElement root, XNamespace ns)
    {
        if (root.Attribute("version")?.Value is { Length: > 0 } version)
        {
            document.SetMetadata("LandXmlVersion", version);
        }

        XElement? units = root.Element(ns + "Units");
        XElement? system = units?.Element(ns + "Metric") ?? units?.Element(ns + "Imperial");

        if (system?.Attribute("linearUnit")?.Value is { Length: > 0 } linearUnit)
        {
            document.Units = linearUnit;
        }

        XElement? crs = root.Element(ns + "CoordinateSystem");

        if (crs is null)
        {
            return;
        }

        // An EPSG code is authoritative; the free-text name is only worth carrying as metadata.
        if (crs.Attribute("epsgCode")?.Value is { Length: > 0 } epsg)
        {
            document.DeclaredCrs = epsg.StartsWith("EPSG:", StringComparison.OrdinalIgnoreCase)
                ? epsg
                : $"EPSG:{epsg}";
        }

        if (crs.Attribute("name")?.Value is { Length: > 0 } name)
        {
            document.SetMetadata("CrsName", name);
        }

        if (crs.Attribute("horizontalDatum")?.Value is { Length: > 0 } datum)
        {
            document.SetMetadata("HorizontalDatum", datum);
        }
    }

    /// <summary>Reads <c>CgPoints</c> into point elements, preserving the survey code.</summary>
    private static int ReadCogoPoints(
        SourceDocument document,
        XElement root,
        XNamespace ns,
        CancellationToken cancellationToken)
    {
        int count = 0;

        foreach (XElement collection in root.Elements(ns + "CgPoints"))
        {
            string layerName = collection.Attribute("name")?.Value is { Length: > 0 } named
                ? $"CgPoints:{named}"
                : "CgPoints";

            foreach (XElement point in collection.Elements(ns + "CgPoint"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!LandXmlGeometry.TryParseCoordinate(point.Value, out Coordinate coordinate))
                {
                    continue;
                }

                string name = point.Attribute("name")?.Value
                    ?? point.Attribute("oID")?.Value
                    ?? $"P{count + 1}";

                SourceElement element = new($"cgpoint:{name}", GeometryKind.Point)
                {
                    Geometry = Factory.CreatePoint(coordinate),
                    NativeType = "CgPoint",
                };

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("PointName", name);
                CopyAttribute(point, "code", element, "Code");
                CopyAttribute(point, "desc", element, "Description");
                CopyAttribute(point, "state", element, "State");
                CopyAttribute(point, "pntSurv", element, "SurveyType");

                if (coordinate is CoordinateZ { Z: var z } && double.IsFinite(z))
                {
                    element.SetAttribute("Elevation", z);
                }

                document.GetOrAddLayer(layerName).AddElement(element);
                count++;
            }
        }

        return count;
    }

    /// <summary>Reads <c>Parcels</c> as closed polygons, preserving area and legal metadata.</summary>
    private static int ReadParcels(
        SourceDocument document,
        XElement root,
        XNamespace ns,
        CancellationToken cancellationToken)
    {
        int count = 0;

        foreach (XElement collection in root.Elements(ns + "Parcels"))
        {
            foreach (XElement parcel in collection.Elements(ns + "Parcel"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<Coordinate> outline = ReadCoordGeom(parcel.Element(ns + "CoordGeom"), ns);

                if (outline.Count < 3)
                {
                    continue;
                }

                string name = parcel.Attribute("name")?.Value ?? $"Parcel{count + 1}";
                string layerName = "Parcels";

                List<Coordinate> ring = [.. outline];

                // A parcel is an area; close the ring if the file left it open, as many do.
                if (!ring[0].Equals2D(ring[^1]))
                {
                    ring.Add(ring[0].Copy());
                }

                SourceElement element = new($"parcel:{name}", GeometryKind.Polygon)
                {
                    NativeType = "Parcel",
                };

                try
                {
                    element.Geometry = Factory.CreatePolygon(Factory.CreateLinearRing([.. ring]));
                }
                catch (ArgumentException)
                {
                    // A ring the geometry library rejects is still worth carrying as its outline
                    // rather than dropping the parcel and its legal description entirely.
                    element.Geometry = Factory.CreateLineString([.. ring]);
                }

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("ParcelName", name);
                CopyAttribute(parcel, "area", element, "Area");
                CopyAttribute(parcel, "desc", element, "Description");
                CopyAttribute(parcel, "parcelType", element, "ParcelType");
                CopyAttribute(parcel, "state", element, "State");
                CopyAttribute(parcel, "class", element, "Class");
                CopyAttribute(parcel, "useOfParcel", element, "UseOfParcel");
                CopyAttribute(parcel, "owner", element, "Owner");

                document.GetOrAddLayer(layerName).AddElement(element);
                count++;
            }
        }

        return count;
    }

    /// <summary>Reads <c>Alignments</c> as linestrings, preserving stationing metadata.</summary>
    private static int ReadAlignments(
        SourceDocument document,
        XElement root,
        XNamespace ns,
        CancellationToken cancellationToken)
    {
        int count = 0;

        foreach (XElement collection in root.Elements(ns + "Alignments"))
        {
            foreach (XElement alignment in collection.Elements(ns + "Alignment"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<Coordinate> path = ReadCoordGeom(alignment.Element(ns + "CoordGeom"), ns);

                if (path.Count < 2)
                {
                    continue;
                }

                string name = alignment.Attribute("name")?.Value ?? $"Alignment{count + 1}";
                const string layerName = "Alignments";

                SourceElement element = new($"alignment:{name}", GeometryKind.Line)
                {
                    Geometry = Factory.CreateLineString([.. path]),
                    NativeType = "Alignment",
                };

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("AlignmentName", name);
                CopyAttribute(alignment, "length", element, "Length");
                CopyAttribute(alignment, "staStart", element, "StartStation");
                CopyAttribute(alignment, "desc", element, "Description");
                CopyAttribute(alignment, "state", element, "State");

                document.GetOrAddLayer(layerName).AddElement(element);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Reads every <c>Surface</c>: its TIN faces, breaklines and boundaries.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A TIN is published as real polygons — one per triangle — rather than as an opaque surface
    /// object. That is what makes a surface work unchanged with everything downstream: each
    /// triangle is a feature the Mapping Editor can draw, the Attribute Table can list, a rule can
    /// match and an exporter can write, with its elevations carried on the vertices.
    /// </para>
    /// <para>
    /// Each surface gets its own layers, suffixed by role, so a drawing with an existing and a
    /// proposed surface keeps them apart in the Project Explorer.
    /// </para>
    /// </remarks>
    private static int ReadSurfaces(
        SourceDocument document,
        XElement root,
        XNamespace ns,
        CancellationToken cancellationToken)
    {
        int count = 0;

        foreach (XElement collection in root.Elements(ns + "Surfaces"))
        {
            foreach (XElement surface in collection.Elements(ns + "Surface"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string name = surface.Attribute("name")?.Value ?? $"Surface{count + 1}";

                count += ReadSurfaceFaces(document, surface, ns, name, cancellationToken);
                count += ReadBreaklines(document, surface, ns, name, cancellationToken);
                count += ReadSurfaceBoundaries(document, surface, ns, name, cancellationToken);
            }
        }

        return count;
    }

    /// <summary>Reads a surface's TIN definition into one polygon per triangle.</summary>
    private static int ReadSurfaceFaces(
        SourceDocument document,
        XElement surface,
        XNamespace ns,
        string surfaceName,
        CancellationToken cancellationToken)
    {
        XElement? definition = surface.Element(ns + "Definition");

        if (definition is null)
        {
            return 0;
        }

        // Point ids are referenced by the faces, so the whole point set is indexed first.
        Dictionary<string, Coordinate> points = new(StringComparer.Ordinal);

        foreach (XElement pointsElement in definition.Elements(ns + "Pnts"))
        {
            foreach (XElement point in pointsElement.Elements(ns + "P"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (point.Attribute("id")?.Value is { Length: > 0 } id
                    && LandXmlGeometry.TryParseCoordinate(point.Value, out Coordinate coordinate))
                {
                    points[id] = coordinate;
                }
            }
        }

        if (points.Count == 0)
        {
            return 0;
        }

        string layerName = $"Surface:{surfaceName}";
        int faces = 0;

        foreach (XElement facesElement in definition.Elements(ns + "Faces"))
        {
            foreach (XElement face in facesElement.Elements(ns + "F"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string[] ids = face.Value.Split(
                    [' ', '\t', '\r', '\n'],
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

                // Coordinate is a reference type, so a missing id yields null; a face that
                // references a point the file never defined is skipped rather than guessed at.
                if (ids.Length < 3
                    || !points.TryGetValue(ids[0], out Coordinate? a) || a is null
                    || !points.TryGetValue(ids[1], out Coordinate? b) || b is null
                    || !points.TryGetValue(ids[2], out Coordinate? c) || c is null)
                {
                    continue;
                }

                IReadOnlyList<Coordinate> ring = LandXmlGeometry.BuildTriangleRing(a, b, c);

                SourceElement element = new($"surface:{surfaceName}:face:{faces + 1}", GeometryKind.Polygon)
                {
                    NativeType = "TinFace",
                };

                try
                {
                    element.Geometry = Factory.CreatePolygon(Factory.CreateLinearRing([.. ring]));
                }
                catch (ArgumentException)
                {
                    // A degenerate face — three collinear or coincident points — is not an area.
                    continue;
                }

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("SurfaceName", surfaceName);
                element.SetAttribute("FaceIndex", faces + 1);
                element.SetAttribute("Elevation", AverageElevation(a, b, c));

                // The neighbour ids a TIN face carries are what makes the mesh navigable later.
                if (face.Attribute("n")?.Value is { Length: > 0 } neighbours)
                {
                    element.SetAttribute("Neighbours", neighbours);
                }

                if (face.Attribute("i")?.Value is { Length: > 0 } invisible)
                {
                    element.SetAttribute("Invisible", invisible);
                }

                document.GetOrAddLayer(layerName).AddElement(element);
                faces++;
            }
        }

        if (faces > 0)
        {
            SourceLayer layer = document.GetOrAddLayer(layerName);
            layer.SetMetadata("SurfaceName", surfaceName);
            layer.SetMetadata("Role", "TIN");

            if (surface.Attribute("desc")?.Value is { Length: > 0 } description)
            {
                layer.SetMetadata("Description", description);
            }
        }

        return faces;
    }

    /// <summary>Reads a surface's breaklines as 3D linestrings.</summary>
    /// <remarks>
    /// Breaklines are the surveyed edges a surface must respect — kerbs, ridges, toes of slope.
    /// They are kept as their own features because they carry engineering meaning the triangles
    /// alone do not.
    /// </remarks>
    private static int ReadBreaklines(
        SourceDocument document,
        XElement surface,
        XNamespace ns,
        string surfaceName,
        CancellationToken cancellationToken)
    {
        string layerName = $"Breaklines:{surfaceName}";
        int count = 0;

        foreach (XElement breaklines in surface.Descendants(ns + "Breaklines"))
        {
            foreach (XElement breakline in breaklines.Elements(ns + "Breakline"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                XElement? list = breakline.Element(ns + "PntList3D") ?? breakline.Element(ns + "PntList2D");
                IReadOnlyList<Coordinate> coordinates = list?.Name.LocalName == "PntList2D"
                    ? ParsePairs(list?.Value)
                    : LandXmlGeometry.ParseCoordinateList(list?.Value);

                if (coordinates.Count < 2)
                {
                    continue;
                }

                string name = breakline.Attribute("name")?.Value ?? $"Breakline{count + 1}";

                SourceElement element = new($"breakline:{surfaceName}:{name}", GeometryKind.Line)
                {
                    Geometry = Factory.CreateLineString([.. coordinates]),
                    NativeType = "Breakline",
                };

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("SurfaceName", surfaceName);
                element.SetAttribute("BreaklineName", name);
                CopyAttribute(breakline, "brkType", element, "BreaklineType");
                CopyAttribute(breakline, "desc", element, "Description");

                document.GetOrAddLayer(layerName).AddElement(element);
                count++;
            }
        }

        if (count > 0)
        {
            document.GetOrAddLayer(layerName).SetMetadata("Role", "Breaklines");
        }

        return count;
    }

    /// <summary>Reads a surface's boundaries as polygons.</summary>
    /// <remarks>
    /// An outer boundary is the surface's extent; a hide boundary is a void within it. Both are
    /// published as polygons and distinguished by their <c>BoundaryType</c> attribute, so a rule
    /// can treat them differently without this reader having to decide for the operator.
    /// </remarks>
    private static int ReadSurfaceBoundaries(
        SourceDocument document,
        XElement surface,
        XNamespace ns,
        string surfaceName,
        CancellationToken cancellationToken)
    {
        string layerName = $"Boundaries:{surfaceName}";
        int count = 0;

        foreach (XElement boundaries in surface.Descendants(ns + "Boundaries"))
        {
            foreach (XElement boundary in boundaries.Elements(ns + "Boundary"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                XElement? list = boundary.Element(ns + "PntList3D") ?? boundary.Element(ns + "PntList2D");
                IReadOnlyList<Coordinate> coordinates = list?.Name.LocalName == "PntList2D"
                    ? ParsePairs(list?.Value)
                    : LandXmlGeometry.ParseCoordinateList(list?.Value);

                if (coordinates.Count < 3)
                {
                    continue;
                }

                IReadOnlyList<Coordinate> ring = LandXmlGeometry.CloseRing(coordinates);
                string name = boundary.Attribute("name")?.Value ?? $"Boundary{count + 1}";

                SourceElement element = new($"boundary:{surfaceName}:{name}", GeometryKind.Polygon)
                {
                    NativeType = "SurfaceBoundary",
                };

                try
                {
                    element.Geometry = Factory.CreatePolygon(Factory.CreateLinearRing([.. ring]));
                }
                catch (ArgumentException)
                {
                    element.Geometry = Factory.CreateLineString([.. ring]);
                }

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("SurfaceName", surfaceName);
                element.SetAttribute("BoundaryName", name);
                CopyAttribute(boundary, "bndType", element, "BoundaryType");
                CopyAttribute(boundary, "edgeTrim", element, "EdgeTrim");
                CopyAttribute(boundary, "desc", element, "Description");

                document.GetOrAddLayer(layerName).AddElement(element);
                count++;
            }
        }

        if (count > 0)
        {
            document.GetOrAddLayer(layerName).SetMetadata("Role", "Boundaries");
        }

        return count;
    }

    /// <summary>
    /// Reads <c>PlanFeatures</c>, the general-purpose planimetric collection.
    /// </summary>
    /// <remarks>
    /// A plan feature is whatever the surveyor needed to record that no stricter collection covers
    /// — fence lines, kerbs, tree canopies, hedges. Geometry family follows the data: a closed run
    /// of coordinates is an area, an open run is a line, and a single point is a point. Guessing
    /// any other way would misrepresent what was surveyed.
    /// </remarks>
    private static int ReadPlanFeatures(
        SourceDocument document,
        XElement root,
        XNamespace ns,
        CancellationToken cancellationToken)
    {
        int count = 0;

        foreach (XElement collection in root.Elements(ns + "PlanFeatures"))
        {
            string groupName = collection.Attribute("name")?.Value is { Length: > 0 } named
                ? $"PlanFeatures:{named}"
                : "PlanFeatures";

            foreach (XElement feature in collection.Elements(ns + "PlanFeature"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                IReadOnlyList<Coordinate> coordinates = ReadCoordGeom(feature.Element(ns + "CoordGeom"), ns);

                if (coordinates.Count == 0)
                {
                    continue;
                }

                string name = feature.Attribute("name")?.Value ?? $"PlanFeature{count + 1}";
                SourceElement element = BuildPlanarElement($"planfeature:{name}", "PlanFeature", coordinates);

                element.SetAttribute("Layer", groupName);
                element.SetAttribute("FeatureName", name);
                CopyAttribute(feature, "desc", element, "Description");
                CopyAttribute(feature, "state", element, "State");

                document.GetOrAddLayer(groupName).AddElement(element);
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Reads <c>PipeNetworks</c>: the structures (manholes, inlets) and the pipes between them.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The connectivity is the point of a utility network, so it is preserved explicitly. Each pipe
    /// records the structures at its ends as <c>StartStructure</c> and <c>EndStructure</c>, and each
    /// structure records its own name, so the graph can be rebuilt downstream without re-reading the
    /// file. That is what will let the semantic layer express "this pipe is connected to that
    /// manhole" when BIM integration needs it.
    /// </para>
    /// <para>
    /// A pipe whose own geometry is absent is reconstructed from the centres of the structures it
    /// joins, which is how most exporters write a straight run.
    /// </para>
    /// </remarks>
    private static int ReadPipeNetworks(
        SourceDocument document,
        XElement root,
        XNamespace ns,
        CancellationToken cancellationToken)
    {
        int count = 0;

        foreach (XElement collection in root.Elements(ns + "PipeNetworks"))
        {
            foreach (XElement network in collection.Elements(ns + "PipeNetwork"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string networkName = network.Attribute("name")?.Value ?? $"Network{count + 1}";
                Dictionary<string, Coordinate> structures = new(StringComparer.OrdinalIgnoreCase);

                count += ReadStructures(document, network, ns, networkName, structures, cancellationToken);
                count += ReadPipes(document, network, ns, networkName, structures, cancellationToken);
            }
        }

        return count;
    }

    /// <summary>Reads a network's structures as points, indexing their centres for the pipes.</summary>
    private static int ReadStructures(
        SourceDocument document,
        XElement network,
        XNamespace ns,
        string networkName,
        Dictionary<string, Coordinate> centres,
        CancellationToken cancellationToken)
    {
        string layerName = $"Structures:{networkName}";
        int count = 0;

        foreach (XElement structures in network.Elements(ns + "Structs"))
        {
            foreach (XElement structure in structures.Elements(ns + "Struct"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                XElement? centre = structure.Element(ns + "Center");

                if (!LandXmlGeometry.TryParseCoordinate(centre?.Value, out Coordinate coordinate))
                {
                    continue;
                }

                string name = structure.Attribute("name")?.Value ?? $"Struct{count + 1}";
                centres[name] = coordinate;

                SourceElement element = new($"struct:{networkName}:{name}", GeometryKind.Point)
                {
                    Geometry = Factory.CreatePoint(coordinate),
                    NativeType = "Struct",
                };

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("NetworkName", networkName);
                element.SetAttribute("StructureName", name);
                CopyAttribute(structure, "desc", element, "Description");
                CopyAttribute(structure, "elevSump", element, "SumpElevation");
                CopyAttribute(structure, "elevRim", element, "RimElevation");
                CopyAttribute(structure, "state", element, "State");

                document.GetOrAddLayer(layerName).AddElement(element);
                count++;
            }
        }

        if (count > 0)
        {
            document.GetOrAddLayer(layerName).SetMetadata("Role", "Structures");
        }

        return count;
    }

    /// <summary>Reads a network's pipes as linestrings, preserving their end connections.</summary>
    private static int ReadPipes(
        SourceDocument document,
        XElement network,
        XNamespace ns,
        string networkName,
        IReadOnlyDictionary<string, Coordinate> centres,
        CancellationToken cancellationToken)
    {
        string layerName = $"Pipes:{networkName}";
        int count = 0;

        foreach (XElement pipes in network.Elements(ns + "Pipes"))
        {
            foreach (XElement pipe in pipes.Elements(ns + "Pipe"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string? startStructure = pipe.Attribute("refStart")?.Value;
                string? endStructure = pipe.Attribute("refEnd")?.Value;

                IReadOnlyList<Coordinate> path = ReadPipeGeometry(pipe, ns, startStructure, endStructure, centres);

                if (path.Count < 2)
                {
                    continue;
                }

                string name = pipe.Attribute("name")?.Value ?? $"Pipe{count + 1}";

                SourceElement element = new($"pipe:{networkName}:{name}", GeometryKind.Line)
                {
                    Geometry = Factory.CreateLineString([.. path]),
                    NativeType = "Pipe",
                };

                element.SetAttribute("Layer", layerName);
                element.SetAttribute("NetworkName", networkName);
                element.SetAttribute("PipeName", name);

                // The connectivity that makes this a network rather than a bundle of lines.
                if (!string.IsNullOrWhiteSpace(startStructure))
                {
                    element.SetAttribute("StartStructure", startStructure);
                }

                if (!string.IsNullOrWhiteSpace(endStructure))
                {
                    element.SetAttribute("EndStructure", endStructure);
                }

                CopyAttribute(pipe, "desc", element, "Description");
                CopyAttribute(pipe, "material", element, "Material");
                CopyAttribute(pipe, "slope", element, "Slope");
                CopyAttribute(pipe, "flowDir", element, "FlowDirection");
                CopyAttribute(pipe, "system", element, "System");

                // Diameter lives on a child element in most exporters, not on the pipe itself.
                if (pipe.Element(ns + "CircPipe") is { } circular)
                {
                    CopyAttribute(circular, "diameter", element, "Diameter");
                    CopyAttribute(circular, "thickness", element, "WallThickness");
                }

                document.GetOrAddLayer(layerName).AddElement(element);
                count++;
            }
        }

        if (count > 0)
        {
            document.GetOrAddLayer(layerName).SetMetadata("Role", "Pipes");
        }

        return count;
    }

    /// <summary>Resolves a pipe's centreline, falling back to the structures it connects.</summary>
    private static IReadOnlyList<Coordinate> ReadPipeGeometry(
        XElement pipe,
        XNamespace ns,
        string? startStructure,
        string? endStructure,
        IReadOnlyDictionary<string, Coordinate> centres)
    {
        // An explicit centreline always wins: it is what the designer drew.
        if (pipe.Element(ns + "PntList3D") is { } list3d)
        {
            IReadOnlyList<Coordinate> explicitPath = LandXmlGeometry.ParseCoordinateList(list3d.Value);

            if (explicitPath.Count >= 2)
            {
                return explicitPath;
            }
        }

        IReadOnlyList<Coordinate> fromCoordGeom = ReadCoordGeom(pipe.Element(ns + "CoordGeom"), ns);

        if (fromCoordGeom.Count >= 2)
        {
            return fromCoordGeom;
        }

        if (startStructure is not null && endStructure is not null
            && centres.TryGetValue(startStructure, out Coordinate? start) && start is not null
            && centres.TryGetValue(endStructure, out Coordinate? end) && end is not null)
        {
            return [start, end];
        }

        return [];
    }

    /// <summary>Chooses the geometry family that matches a run of coordinates.</summary>
    private static SourceElement BuildPlanarElement(
        string id,
        string nativeType,
        IReadOnlyList<Coordinate> coordinates)
    {
        if (coordinates.Count == 1)
        {
            return new SourceElement(id, GeometryKind.Point)
            {
                Geometry = Factory.CreatePoint(coordinates[0]),
                NativeType = nativeType,
            };
        }

        bool closed = coordinates.Count >= 4 && coordinates[0].Equals2D(coordinates[^1]);

        if (closed)
        {
            SourceElement area = new(id, GeometryKind.Polygon) { NativeType = nativeType };

            try
            {
                area.Geometry = Factory.CreatePolygon(Factory.CreateLinearRing([.. coordinates]));

                return area;
            }
            catch (ArgumentException)
            {
                // Falls through to a line: a ring the library rejects is still a surveyed run.
            }
        }

        return new SourceElement(id, GeometryKind.Line)
        {
            Geometry = Factory.CreateLineString([.. coordinates]),
            NativeType = nativeType,
        };
    }

    /// <summary>Parses a flat run of northing/easting pairs, as used by <c>PntList2D</c>.</summary>
    private static IReadOnlyList<Coordinate> ParsePairs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return [];
        }

        string[] parts = text.Split(
            [' ', '\t', '\r', '\n'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        List<Coordinate> coordinates = [];

        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            if (double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double northing)
                && double.TryParse(parts[i + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out double easting))
            {
                coordinates.Add(new Coordinate(easting, northing));
            }
        }

        return coordinates;
    }

    /// <summary>Averages the elevations of a face's vertices, ignoring any that are not set.</summary>
    private static double AverageElevation(Coordinate a, Coordinate b, Coordinate c)
    {
        double total = 0d;
        int counted = 0;

        foreach (Coordinate coordinate in new[] { a, b, c })
        {
            if (double.IsFinite(coordinate.Z))
            {
                total += coordinate.Z;
                counted++;
            }
        }

        return counted == 0 ? 0d : total / counted;
    }

    /// <summary>
    /// Walks a <c>CoordGeom</c> block, following lines, curves and spirals in document order.
    /// </summary>
    /// <remarks>
    /// A spiral's true clothoid is approximated by its chord: the alternative is a transition-curve
    /// solver, and for a plan-view GIS deliverable the chord is within the tolerance the output is
    /// drawn at. Lines and circular curves are exact.
    /// </remarks>
    private static IReadOnlyList<Coordinate> ReadCoordGeom(XElement? coordGeom, XNamespace ns)
    {
        if (coordGeom is null)
        {
            return [];
        }

        List<Coordinate> path = [];

        foreach (XElement segment in coordGeom.Elements())
        {
            switch (segment.Name.LocalName)
            {
                case "Line":
                    AppendPoint(path, segment.Element(ns + "Start")?.Value);
                    AppendPoint(path, segment.Element(ns + "End")?.Value);
                    break;

                case "Curve":
                    AppendCurve(path, segment, ns);
                    break;

                case "Spiral":
                    AppendPoint(path, segment.Element(ns + "Start")?.Value);
                    AppendPoint(path, segment.Element(ns + "End")?.Value);
                    break;
            }
        }

        return path;
    }

    private static void AppendCurve(List<Coordinate> path, XElement curve, XNamespace ns)
    {
        bool hasStart = LandXmlGeometry.TryParseCoordinate(curve.Element(ns + "Start")?.Value, out Coordinate start);
        bool hasCentre = LandXmlGeometry.TryParseCoordinate(curve.Element(ns + "Center")?.Value, out Coordinate centre);
        bool hasEnd = LandXmlGeometry.TryParseCoordinate(curve.Element(ns + "End")?.Value, out Coordinate end);

        if (!hasStart || !hasEnd)
        {
            return;
        }

        if (!hasCentre)
        {
            // Without a centre the arc is undefined; its chord is the honest fallback.
            LandXmlGeometry.AppendWithoutDuplicate(path, [start, end]);
            return;
        }

        bool clockwise = string.Equals(curve.Attribute("rot")?.Value, "cw", StringComparison.OrdinalIgnoreCase);

        LandXmlGeometry.AppendWithoutDuplicate(
            path,
            LandXmlGeometry.TessellateArc(start, centre, end, clockwise));
    }

    private static void AppendPoint(List<Coordinate> path, string? text)
    {
        if (LandXmlGeometry.TryParseCoordinate(text, out Coordinate coordinate)
            && (path.Count == 0 || !path[^1].Equals2D(coordinate)))
        {
            path.Add(coordinate);
        }
    }

    /// <summary>Copies an XML attribute onto an element, typing numbers where possible.</summary>
    private static void CopyAttribute(XElement source, string attributeName, SourceElement target, string name)
    {
        if (source.Attribute(attributeName)?.Value is not { Length: > 0 } value)
        {
            return;
        }

        target.SetAttribute(
            name,
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double number)
                ? number
                : value);
    }
}
