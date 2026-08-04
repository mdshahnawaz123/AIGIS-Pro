using System.Globalization;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using Xbim.Ifc;
using Xbim.Ifc4.Interfaces;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsGeometryFactory = NetTopologySuite.Geometries.GeometryFactory;

namespace AiGisConverter.Plugins.Ifc;

/// <summary>
/// IFC Reader, bound to xBIM Essentials.
/// </summary>
/// <remarks>
/// <para>
/// Reads <c>.ifc</c> / <c>.ifcxml</c> / <c>.ifczip</c> through the schema-neutral
/// <c>Xbim.Ifc4.Interfaces</c> layer, so the same code path serves IFC2x3, IFC4 and IFC4x3. Each
/// <see cref="IIfcProduct"/> becomes a <see cref="SourceElement"/> carrying the attributes the
/// generic pipeline and <see cref="IfcSemanticProvider"/> consume: express type, name, object type,
/// predefined type, containing storey, material and quantity values.
/// </para>
/// <para>
/// Geometry in this phase is the element's resolved placement <em>point</em>, derived by walking the
/// <see cref="IIfcLocalPlacement"/> chain. Full solid tessellation (BREP / swept-solid to mesh or
/// footprint) needs the xBIM geometry engine, which is a Windows-only native component; it is out of
/// scope here by design and is the subject of the later 3D phase. An element whose placement cannot
/// be resolved is emitted with no geometry, and the GIS pipeline substitutes a valid empty geometry
/// rather than exporting a null one.
/// </para>
/// </remarks>
internal sealed class IfcReader : IDataSourceReader
{
    private static readonly NtsGeometryFactory _factory = new();

    private readonly IPluginContext _context;

    /// <summary>Initializes a new instance of the <see cref="IfcReader"/> class.</summary>
    /// <param name="context">The plugin context.</param>
    public IfcReader(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    /// Gets a value indicating whether the format backend is bound in this build.
    /// </summary>
    public static bool IsBackendAvailable => true;

    /// <inheritdoc />
    public string FormatKey => "ifc";

    /// <inheritdoc />
    public string DisplayName => "IFC Reader";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = [".ifc", ".ifcxml", ".ifczip"];

    /// <inheritdoc />
    public bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase)
               && File.Exists(reference.Location);
    }

    /// <inheritdoc />
    public Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        // xBIM parsing is synchronous and CPU-bound; wrapping it keeps the caller's thread free
        // without pretending the underlying library is asynchronous. Mirrors DxfProvider.
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
                "Ifc.FileNotFound",
                $"'{reference.Location}' does not exist."));
        }

        progress?.Report(new ReadProgress(0d, "Opening IFC model..."));

        IfcStore? model = null;

        try
        {
            model = IfcStore.Open(reference.Location);

            SourceDocument document = new(reference, "ifc");
            ApplyModelMetadata(document, model);

            progress?.Report(new ReadProgress(0.1d, "Reading products..."));

            int emitted = 0;

            // Every product resolves six inverse attributes - ContainedInStructure, Decomposes,
            // IsDefinedBy, IsTypedBy, HasAssociations and FillsVoids. Uncached, xBIM answers each
            // by scanning the relationship instances and testing set membership, so the cost per
            // element grows with the model and the read is quadratic overall. Measured on a
            // generated model with production-shaped relationship sizes, four times the elements
            // took 16.45 times the time - 500ms at 5,000 against 8,230ms at 20,000.
            //
            // The cache builds the reverse index once and answers from it. Its lifetime is scoped
            // to the traversal alone: it is worth real memory on a large model, and nothing after
            // this loop reads an inverse.
            using (model.BeginInverseCaching())
            {
                foreach (IIfcProduct product in model.Instances.OfType<IIfcProduct>())
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Spatial containers are emitted alongside the building elements. Carrying a
                    // storey only as a string attribute on its children left the semantic graph with
                    // nothing to hang a Contains relationship from: the referenced storey simply was
                    // not in it. A site, building, storey or space is also something an operator
                    // wants to select, filter and report on in its own right.
                    SourceElement? element = TryConvert(product, document);

                    if (element is null)
                    {
                        continue;
                    }

                    document.GetOrAddLayer(LayerNameFor(element)).AddElement(element);
                    emitted++;

                    if (emitted % 2000 == 0)
                    {
                        progress?.Report(new ReadProgress(null, $"Read {emitted:N0} products..."));
                    }
                }
            }

            progress?.Report(new ReadProgress(1d, $"Read {emitted:N0} products."));

            _context.Logger.LogInformation(
                "Read {ProductCount} IFC products across {LayerCount} layers from {File} ({WarningCount} warnings).",
                emitted,
                document.Layers.Count,
                Path.GetFileName(reference.Location),
                document.Warnings.Count);

            return Result.Success(document);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // xBIM surfaces malformed models and unsupported schemas through several exception
            // types; a bad file must fail as one clear Result, never abort a batch run.
            _context.Logger.LogWarning(ex, "The IFC model at {File} could not be read.", reference.Location);

            return Result.Failure<SourceDocument>(new Error(
                "Ifc.ReadFailed",
                $"'{Path.GetFileName(reference.Location)}' could not be read as IFC: {ex.Message}"));
        }
        finally
        {
            model?.Dispose();
        }
    }

    /// <summary>Records the model's schema and project identity as document metadata.</summary>
    private static void ApplyModelMetadata(SourceDocument document, IfcStore model)
    {
        document.SetMetadata("IfcSchema", model.SchemaVersion.ToString());

        IIfcProject? project = model.Instances.OfType<IIfcProject>().FirstOrDefault();

        if (project is null)
        {
            return;
        }

        if (project.Name.HasValue)
        {
            document.SetMetadata("IfcProject", project.Name.Value.ToString() ?? string.Empty);
        }

        AttachUnits(document, project);

        // A georeferenced IFC declares its CRS through IfcProjectedCRS (IFC4). Carry the name
        // forward for the GIS layer to interpret; the pipeline still runs when it is absent.
        IIfcCoordinateReferenceSystem? crs = model.Instances.OfType<IIfcCoordinateReferenceSystem>().FirstOrDefault();

        if (crs?.Name is { } crsName && !string.IsNullOrWhiteSpace(crsName))
        {
            document.DeclaredCrs = crsName;
        }
    }

    /// <summary>
    /// Records the model's unit assignment, and the length unit as the document's units.
    /// </summary>
    /// <remarks>
    /// Every length, area and volume read from the model is expressed in these units. Without them
    /// the numbers are ambiguous — a wall "3.0" long is three metres or three millimetres depending
    /// on a declaration that lives only here.
    /// </remarks>
    private static void AttachUnits(SourceDocument document, IIfcProject project)
    {
        if (project.UnitsInContext is not { } assignment)
        {
            return;
        }

        foreach (IIfcUnit unit in assignment.Units)
        {
            if (unit is not IIfcNamedUnit named)
            {
                continue;
            }

            string description = Describe(named);

            switch (named.UnitType)
            {
                case IfcUnitEnum.LENGTHUNIT:
                    document.Units = description;
                    document.SetMetadata("LengthUnit", description);
                    break;
                case IfcUnitEnum.AREAUNIT:
                    document.SetMetadata("AreaUnit", description);
                    break;
                case IfcUnitEnum.VOLUMEUNIT:
                    document.SetMetadata("VolumeUnit", description);
                    break;
                case IfcUnitEnum.PLANEANGLEUNIT:
                    document.SetMetadata("AngleUnit", description);
                    break;
            }
        }
    }

    /// <summary>Describes a unit, including its SI prefix where one is declared.</summary>
    private static string Describe(IIfcNamedUnit unit) => unit switch
    {
        IIfcSIUnit si => si.Prefix is { } prefix
            ? $"{prefix.ToString()!.ToLowerInvariant()}{si.Name.ToString().ToLowerInvariant()}"
            : si.Name.ToString().ToLowerInvariant(),
        IIfcConversionBasedUnit converted => converted.Name.ToString(),
        _ => unit.UnitType.ToString(),
    };

    /// <summary>Converts one IFC product into a domain element, or null when it should be skipped.</summary>
    private SourceElement? TryConvert(IIfcProduct product, SourceDocument document)
    {
        try
        {
            string globalId = product.GlobalId.ToString();
            string id = string.IsNullOrWhiteSpace(globalId)
                ? product.EntityLabel.ToString(CultureInfo.InvariantCulture)
                : globalId;

            NtsGeometry? geometry = TryResolvePlacementPoint(product.ObjectPlacement);

            SourceElement element = new(id, GeometryKind.Point)
            {
                Geometry = geometry,
                NativeType = product.ExpressType.ExpressName,
                Text = product.Name.HasValue ? product.Name.Value.ToString() : null,
            };

            PopulateAttributes(element, product);

            return element;
        }
        catch (Exception ex)
        {
            // One unreadable product must not lose the rest of the model.
            document.AddWarning($"An IFC product could not be converted and was skipped: {ex.Message}");
            return null;
        }
    }

    /// <summary>Reads the identity, spatial and property-set attributes the pipeline consumes.</summary>
    private static void PopulateAttributes(SourceElement element, IIfcProduct product)
    {
        element.SetAttribute("GlobalId", product.GlobalId.ToString());
        element.SetAttribute("Name", product.Name.HasValue ? product.Name.Value.ToString() : null);
        element.SetAttribute("IfcType", product.ExpressType.ExpressName);

        if (product is IIfcObject obj && obj.ObjectType.HasValue)
        {
            element.SetAttribute("ObjectType", obj.ObjectType.Value.ToString());
        }

        string? predefined = TryReadPredefinedType(product);

        if (predefined is not null)
        {
            element.SetAttribute("PredefinedType", predefined);
        }

        AttachSpatialIdentity(element, product);
        AttachSpatialContainer(element, product);
        AttachHost(element, product);
        AttachMaterial(element, product);
        AttachClassification(element, product);
        AttachTypeObject(element, product);
        AttachPropertiesAndQuantities(element, product);
    }

    /// <summary>
    /// Records the type this occurrence is an instance of, and the properties it inherits from it.
    /// </summary>
    /// <remarks>
    /// Most of a BIM element's properties live on its type, not the occurrence: a hundred doors of
    /// one type carry their fire rating once, on the type. Reading only the occurrence's own
    /// property sets therefore misses the majority of the data. Occurrence values are written after
    /// the inherited ones so a per-instance override still wins.
    /// </remarks>
    private static void AttachTypeObject(SourceElement element, IIfcProduct product)
    {
        if (product is not IIfcObject ifcObject)
        {
            return;
        }

        IIfcTypeObject? type = ifcObject.IsTypedBy
            .Select(static rel => rel.RelatingType)
            .FirstOrDefault();

        if (type is null)
        {
            return;
        }

        element.SetAttribute("TypeName", type.Name.HasValue ? type.Name.Value.ToString() : null);
        element.SetAttribute("TypeGlobalId", type.GlobalId.ToString());
        element.SetAttribute("TypeIfcClass", type.ExpressType.ExpressName);

        foreach (IIfcPropertySetDefinition definition in type.HasPropertySets)
        {
            CopyPropertySet(element, definition);
        }
    }

    /// <summary>Records any classification reference associated with the element.</summary>
    /// <remarks>
    /// Classification is how a model states "this is Uniclass EF_25_10" — the vocabulary a GIS
    /// consumer most often wants to map onto its own feature classes, so it is carried through
    /// rather than left in the file.
    /// </remarks>
    private static void AttachClassification(SourceElement element, IIfcProduct product)
    {
        if (product is not IIfcObjectDefinition definition)
        {
            return;
        }

        IIfcClassificationSelect? classification = definition.HasAssociations
            .OfType<IIfcRelAssociatesClassification>()
            .Select(static rel => rel.RelatingClassification)
            .FirstOrDefault();

        switch (classification)
        {
            case IIfcClassificationReference reference:
                // Identification and Name are inherited from IIfcExternalReference and are
                // nullable; IIfcClassification.Name is not. The two must not be treated alike.
                element.SetAttribute("ClassificationCode", reference.Identification?.ToString());
                element.SetAttribute("ClassificationName", reference.Name?.ToString());

                if (reference.ReferencedSource is IIfcClassification referencedSystem)
                {
                    element.SetAttribute("ClassificationSystem", referencedSystem.Name.ToString());
                }

                break;

            case IIfcClassification classificationSystem:
                element.SetAttribute("ClassificationSystem", classificationSystem.Name.ToString());
                break;
        }
    }

    /// <summary>
    /// Marks spatial containers and records their parent in the hierarchy.
    /// </summary>
    /// <remarks>
    /// Site, building, storey and space form the tree everything else hangs from. The parent comes
    /// from <c>IfcRelAggregates</c> — the decomposition relationship — which is what makes a storey
    /// resolvable to its building rather than floating on its own.
    /// </remarks>
    private static void AttachSpatialIdentity(SourceElement element, IIfcProduct product)
    {
        if (product is not IIfcSpatialStructureElement spatial)
        {
            return;
        }

        element.SetAttribute("IsSpatialElement", true);
        element.SetAttribute("SpatialType", spatial.ExpressType.ExpressName);

        // LongName is declared on IIfcSpatialElement, the parent of the structural variant, and is
        // where storeys and spaces usually carry their human-readable title.
        if (product is IIfcSpatialElement { LongName: { } longName })
        {
            element.SetAttribute("LongName", longName.ToString());
        }

        IIfcObjectDefinition? parent = spatial.Decomposes
            .Select(static rel => rel.RelatingObject)
            .FirstOrDefault();

        if (parent is not null)
        {
            element.SetAttribute("ParentId", parent.GlobalId.ToString());
            element.SetAttribute("ParentName", parent.Name.HasValue ? parent.Name.Value.ToString() : null);
        }
    }

    /// <summary>
    /// Records the element a door or window is hosted by.
    /// </summary>
    /// <remarks>
    /// A door does not reference its wall directly: it fills an opening, and the opening voids the
    /// wall. Following that two-step chain is what lets the semantic graph express "this door is
    /// hosted by that wall", which the QA rules and the BIM integration both need.
    /// </remarks>
    private static void AttachHost(SourceElement element, IIfcProduct product)
    {
        if (product is not IIfcElement ifcElement)
        {
            return;
        }

        // VoidsElements is a single relation, not a collection: an opening voids exactly one
        // element. Treating it as a sequence does not compile and would misread the schema.
        IIfcElement? host = ifcElement.FillsVoids
            .Select(static fills => fills.RelatingOpeningElement)
            .Select(static opening => opening.VoidsElements)
            .Where(static voids => voids is not null)
            .Select(static voids => voids.RelatingBuildingElement)
            .FirstOrDefault();

        if (host is null)
        {
            return;
        }

        element.SetAttribute("HostId", host.GlobalId.ToString());
        element.SetAttribute("HostName", host.Name.HasValue ? host.Name.Value.ToString() : null);
        element.SetAttribute("HostType", host.ExpressType.ExpressName);
    }

    /// <summary>Names the containing storey and links the element to it for the semantic graph.</summary>
    private static void AttachSpatialContainer(SourceElement element, IIfcProduct product)
    {
        if (product is not IIfcElement ifcElement)
        {
            return;
        }

        IIfcSpatialStructureElement? container = (IIfcSpatialStructureElement?)ifcElement.ContainedInStructure
            .Select(static rel => rel.RelatingStructure)
            .FirstOrDefault();

        if (container is IIfcBuildingStorey storey)
        {
            element.SetAttribute("BuildingStorey", storey.Name.HasValue ? storey.Name.Value.ToString() : null);
            element.SetAttribute("ContainedInStoreyId", storey.GlobalId.ToString());

            if (storey.Elevation.HasValue)
            {
                element.SetAttribute("Elevation", (double)storey.Elevation.Value);
            }
        }
        else if (container is not null)
        {
            element.SetAttribute("SpatialContainer", container.Name.HasValue ? container.Name.Value.ToString() : null);
        }
    }

    /// <summary>Reads the first associated material name, when one is present.</summary>
    private static void AttachMaterial(SourceElement element, IIfcProduct product)
    {
        if (product is not IIfcObjectDefinition definition)
        {
            return;
        }

        IIfcMaterialSelect? material = definition.HasAssociations
            .OfType<IIfcRelAssociatesMaterial>()
            .Select(static rel => rel.RelatingMaterial)
            .FirstOrDefault();

        string? name = material switch
        {
            IIfcMaterial single => single.Name.ToString(),
            IIfcMaterialLayerSetUsage usage => usage.ForLayerSet?.LayerSetName?.ToString(),
            IIfcMaterialLayerSet set => set.LayerSetName?.ToString(),
            _ => null,
        };

        if (!string.IsNullOrWhiteSpace(name))
        {
            element.SetAttribute("Material", name);
        }
    }

    /// <summary>Copies property-set single values and element quantities onto the element.</summary>
    private static void AttachPropertiesAndQuantities(SourceElement element, IIfcProduct product)
    {
        if (product is not IIfcObject ifcObject)
        {
            return;
        }

        // RelatingPropertyDefinition is an IfcPropertySetDefinitionSelect: in IFC4 it can be a
        // single set or a set-of-sets, and in IFC2x3 a single definition. PropertySetDefinitions
        // flattens all three into the actual definitions - the idiom xBIM itself uses - so no
        // grouped property sets are silently missed.
        foreach (IIfcRelDefinesByProperties relation in ifcObject.IsDefinedBy)
        {
            foreach (IIfcPropertySetDefinition definition in relation.RelatingPropertyDefinition.PropertySetDefinitions)
            {
                CopyPropertySet(element, definition);
            }
        }
    }

    /// <summary>
    /// Copies one property set or quantity set onto the element.
    /// </summary>
    /// <remarks>
    /// Shared by the occurrence and the type so both are read identically — a property inherited
    /// from a type must behave exactly like one set on the instance. Nested sets are followed,
    /// because <c>IfcComplexProperty</c> is how exporters group related values and the leaves are
    /// where the data actually is.
    /// </remarks>
    /// <param name="element">The element being populated.</param>
    /// <param name="definition">The property or quantity set to copy.</param>
    private static void CopyPropertySet(SourceElement element, IIfcPropertySetDefinition definition)
    {
        switch (definition)
        {
            case IIfcPropertySet set:
                foreach (IIfcProperty property in set.HasProperties)
                {
                    CopyProperty(element, property);
                }

                break;

            case IIfcElementQuantity quantity:
                AttachQuantities(element, quantity);
                break;
        }
    }

    /// <summary>Copies a single property, descending into complex ones.</summary>
    /// <param name="element">The element being populated.</param>
    /// <param name="property">The property to copy.</param>
    private static void CopyProperty(SourceElement element, IIfcProperty property)
    {
        switch (property)
        {
            case IIfcPropertySingleValue { NominalValue: { } value }:
                element.SetAttribute(property.Name.ToString(), value.Value);
                break;

            case IIfcComplexProperty complex:
                // Nested set: its leaves carry the values, so recurse rather than record the group.
                foreach (IIfcProperty nested in complex.HasProperties)
                {
                    CopyProperty(element, nested);
                }

                break;

            case IIfcPropertyEnumeratedValue { EnumerationValues: { } values } when values.Any():
                element.SetAttribute(
                    property.Name.ToString(),
                    string.Join(", ", values.Select(static v => v.Value?.ToString())));
                break;

            case IIfcPropertyListValue { ListValues: { } list } when list.Any():
                element.SetAttribute(
                    property.Name.ToString(),
                    string.Join(", ", list.Select(static v => v.Value?.ToString())));
                break;
        }
    }

    /// <summary>Maps the standard physical quantities onto the names the semantic layer reads.</summary>
    private static void AttachQuantities(SourceElement element, IIfcElementQuantity quantity)
    {
        foreach (IIfcPhysicalQuantity measure in quantity.Quantities)
        {
            switch (measure)
            {
                case IIfcQuantityArea area:
                    element.SetAttribute("Area", (double)area.AreaValue);
                    break;
                case IIfcQuantityLength length:
                    element.SetAttribute("Length", (double)length.LengthValue);
                    break;
                case IIfcQuantityVolume volume:
                    element.SetAttribute("Volume", (double)volume.VolumeValue);
                    break;
            }
        }
    }

    /// <summary>
    /// Reads a product's <c>PredefinedType</c> without binding to a specific schema type.
    /// </summary>
    /// <remarks>
    /// Every leaf element type declares its own <c>PredefinedType</c> enum, so there is no shared
    /// interface member to read. Reflection over the single well-known property name keeps this one
    /// concern out of a switch over dozens of element types.
    /// </remarks>
    private static string? TryReadPredefinedType(IIfcProduct product)
    {
        object? value = product.GetType()
            .GetProperty("PredefinedType")?
            .GetValue(product);

        return value?.ToString();
    }

    /// <summary>
    /// Resolves an object placement to a world-space point by summing the local placement chain.
    /// </summary>
    /// <remarks>
    /// Only the translation of each <see cref="IIfcAxis2Placement3D"/> is accumulated. Rotation is
    /// ignored deliberately: a single placement marker does not need it, and honouring it correctly
    /// belongs with full geometry tessellation in the later 3D phase.
    /// </remarks>
    private static NtsGeometry? TryResolvePlacementPoint(IIfcObjectPlacement? placement)
    {
        double x = 0d, y = 0d, z = 0d;
        bool resolved = false;
        IIfcObjectPlacement? current = placement;
        int guard = 0;

        while (current is IIfcLocalPlacement local && guard++ < 256)
        {
            if (local.RelativePlacement is IIfcAxis2Placement3D axis && axis.Location is IIfcCartesianPoint point)
            {
                x += point.X;
                y += point.Y;
                z += point.Z;
                resolved = true;
            }

            current = local.PlacementRelTo;
        }

        if (!resolved)
        {
            return null;
        }

        return double.IsNaN(z) || z == 0d
            ? _factory.CreatePoint(new Coordinate(x, y))
            : _factory.CreatePoint(new CoordinateZ(x, y, z));
    }

    /// <summary>Files elements onto a layer named for their IFC type, e.g. <c>IfcWall</c>.</summary>
    private static string LayerNameFor(SourceElement element) =>
        string.IsNullOrWhiteSpace(element.NativeType) ? "IfcProduct" : element.NativeType!;
}
