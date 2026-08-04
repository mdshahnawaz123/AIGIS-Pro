using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.Revit.DB;
using AiGisConverter.Bridge.Protocol;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Reads an open Revit document into the bridge wire format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every method here runs on Revit's thread, marshalled through <see cref="RevitJobQueue"/>.
    /// Nothing on this type is safe to call from the bridge's listener thread.
    /// </para>
    /// <para>
    /// Geometry is extracted as a plan footprint for the supported categories only. The target
    /// model is two-dimensional, so a solid crosses the bridge as its outline with its height
    /// alongside as an attribute. Categories outside the supported set are still enumerated, with
    /// their identity and parameters intact and no geometry - element counts must not change
    /// because geometry was added.
    /// </para>
    /// </remarks>
    internal static class RevitDocumentReader
    {
        /// <summary>The format key the converter's Revit reader declares.</summary>
        internal const string FormatKey = "rvt";

        /// <summary>Reads a document into its wire form.</summary>
        /// <param name="document">The open Revit document.</param>
        /// <returns>The wire document.</returns>
        internal static BridgeDocument Read(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            BridgeDocument wire = new BridgeDocument
            {
                Location = string.IsNullOrEmpty(document.PathName) ? document.Title : document.PathName,
                FormatKey = FormatKey,
                Units = ReadLengthUnit(document, out string unitWarning),
            };

            if (unitWarning != null)
            {
                wire.Warnings.Add(unitWarning);
            }

            AddDocumentMetadata(wire, document);

            // One extractor per read, so the family footprint cache lives exactly as long as the
            // document traversal that populates it and never spans two models.
            using (RevitGeometryExtractor extractor = new RevitGeometryExtractor())
            {
                AddElements(wire, document, extractor);

                Set(wire.Metadata, "GeometryCacheHits",
                    extractor.CacheHits.ToString(CultureInfo.InvariantCulture));
                Set(wire.Metadata, "GeometrySimplifiedRings",
                    extractor.SimplifiedRings.ToString(CultureInfo.InvariantCulture));
            }

            return wire;
        }

        /// <summary>Describes the documents currently open, for the listOpenDocuments method.</summary>
        /// <param name="documents">The open documents.</param>
        /// <returns>A title-to-path map.</returns>
        internal static Dictionary<string, string> DescribeOpenDocuments(IEnumerable<Document> documents)
        {
            Dictionary<string, string> described =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            if (documents == null)
            {
                return described;
            }

            foreach (Document document in documents)
            {
                if (document == null || document.IsFamilyDocument)
                {
                    continue;
                }

                described[document.Title] = document.PathName ?? string.Empty;
            }

            return described;
        }

        private static void AddDocumentMetadata(BridgeDocument wire, Document document)
        {
            Set(wire.Metadata, "Title", document.Title);
            Set(wire.Metadata, "PathName", document.PathName);
            Set(wire.Metadata, "IsWorkshared", document.IsWorkshared.ToString(CultureInfo.InvariantCulture));

            ProjectInfo information = document.ProjectInformation;

            if (information == null)
            {
                wire.Warnings.Add("The document has no Project Information element.");
                return;
            }

            // Read through the ProjectInfo properties rather than by parameter name: the parameter
            // names are localised, and a model authored in a non-English Revit would silently
            // produce none of these.
            Set(wire.Metadata, "ProjectName", information.Name);
            Set(wire.Metadata, "ProjectNumber", information.Number);
            Set(wire.Metadata, "ProjectStatus", information.Status);
            Set(wire.Metadata, "ProjectAddress", information.Address);
            Set(wire.Metadata, "ClientName", information.ClientName);
            Set(wire.Metadata, "BuildingName", information.BuildingName);
            Set(wire.Metadata, "Author", information.Author);
            Set(wire.Metadata, "OrganizationName", information.OrganizationName);
            Set(wire.Metadata, "OrganizationDescription", information.OrganizationDescription);
            Set(wire.Metadata, "IssueDate", information.IssueDate);
        }

        private static string ReadLengthUnit(Document document, out string warning)
        {
            warning = null;

            try
            {
                Units units = document.GetUnits();
                FormatOptions format = units.GetFormatOptions(SpecTypeId.Length);
                ForgeTypeId unit = format.GetUnitTypeId();

                // The catalog string is stable and machine-readable ("millimeters"); the label is
                // localised. Downstream compares units, so the stable form is the right one.
                return UnitUtils.GetTypeCatalogStringForUnit(unit);
            }
            catch (Exception exception)
            {
                // A model whose length unit cannot be read is still worth importing; a length with
                // no declared unit is ambiguous, so it is recorded rather than guessed.
                warning = "The document's length unit could not be read: " + exception.Message;
                return null;
            }
        }

        private static void AddElements(
            BridgeDocument wire,
            Document document,
            RevitGeometryExtractor extractor)
        {
            Dictionary<string, BridgeLayer> layers =
                new Dictionary<string, BridgeLayer>(StringComparer.OrdinalIgnoreCase);

            FilteredElementCollector collector =
                new FilteredElementCollector(document).WhereElementIsNotElementType();

            foreach (Element element in collector)
            {
                if (!IsModelElement(element))
                {
                    continue;
                }

                BridgeElement wireElement;

                try
                {
                    wireElement = MapElement(document, element, extractor, wire);
                }
                catch (Exception exception)
                {
                    // Mirrors BridgeDocumentMapper on the receiving side: record the casualty and
                    // keep going. Losing one element is recoverable; losing the model is not.
                    wire.Warnings.Add(
                        "Element " + element.Id.ToString() + " could not be read: " + exception.Message);
                    continue;
                }

                if (wireElement == null)
                {
                    continue;
                }

                string layerName = element.Category == null ? "Uncategorized" : element.Category.Name;
                BridgeLayer layer;

                if (!layers.TryGetValue(layerName, out layer))
                {
                    layer = new BridgeLayer { Name = layerName };
                    layers[layerName] = layer;
                    wire.Layers.Add(layer);
                }

                layer.Elements.Add(wireElement);
            }
        }

        /// <summary>Decides whether an element belongs in the exported model.</summary>
        /// <remarks>
        /// View-specific annotation, sketch geometry and internal bookkeeping elements are excluded.
        /// Levels are deliberately kept: the semantic graph hangs a Contains relationship off a
        /// level, and a relationship whose target is absent from the document is not a relationship
        /// at all - the same lesson the IFC reader learned about spatial containers.
        /// </remarks>
        /// <param name="element">The candidate element.</param>
        /// <returns><see langword="true"/> when the element should be exported.</returns>
        private static bool IsModelElement(Element element)
        {
            if (element == null || element.Category == null)
            {
                return false;
            }

            if (element.Category.CategoryType != CategoryType.Model)
            {
                return false;
            }

            // View-specific elements are drafting, not model content.
            return element.ViewSpecific == false;
        }

        private static BridgeElement MapElement(
            Document document,
            Element element,
            RevitGeometryExtractor extractor,
            BridgeDocument wireDocument)
        {
            BridgeElement wire = new BridgeElement
            {
                // UniqueId, not ElementId. The integer id is only stable within one session of one
                // model; UniqueId survives save, reload and worksharing, which is what a downstream
                // feature key has to do.
                Id = element.UniqueId,

                GeometryKind = "Unknown",
                GeometryWkt = null,
                NativeType = element.GetType().Name,
            };

            // The document is passed in only so a geometry warning has somewhere to be recorded.
            AddGeometry(element, extractor, wireDocument, wire);

            // Not IntegerValue: Revit 2024 widened ElementId to 64-bit and deprecated it, and a
            // deprecation warning is a build error here. ToString carries the same value and does
            // not pin the add-in to one release.
            Set(wire.Attributes, "ElementId", element.Id.ToString());
            Set(wire.Attributes, "Name", SafeName(element));
            Set(wire.Attributes, "Category", element.Category == null ? null : element.Category.Name);

            AddTypeAndFamily(document, element, wire);
            AddLevel(document, element, wire);
            AddHost(element, wire);
            AddQuantities(element, wire);

            return wire;
        }

        private static void AddTypeAndFamily(Document document, Element element, BridgeElement wire)
        {
            ElementId typeId = element.GetTypeId();

            if (typeId == null || typeId == ElementId.InvalidElementId)
            {
                return;
            }

            ElementType type = document.GetElement(typeId) as ElementType;

            if (type == null)
            {
                return;
            }

            Set(wire.Attributes, "Type", SafeName(type));
            Set(wire.Attributes, "TypeId", type.UniqueId);
            Set(wire.Attributes, "Family", type.FamilyName);
        }

        private static void AddLevel(Document document, Element element, BridgeElement wire)
        {
            ElementId levelId = element.LevelId;

            if (levelId == null || levelId == ElementId.InvalidElementId)
            {
                return;
            }

            Element level = document.GetElement(levelId);

            if (level == null)
            {
                return;
            }

            Set(wire.Attributes, "Level", SafeName(level));

            // The UniqueId, because the semantic provider resolves this against feature ids - which
            // are UniqueIds. An integer here would look plausible and match nothing.
            Set(wire.Attributes, "LevelId", level.UniqueId);
        }

        private static void AddHost(Element element, BridgeElement wire)
        {
            FamilyInstance instance = element as FamilyInstance;

            if (instance == null || instance.Host == null)
            {
                return;
            }

            Set(wire.Attributes, "HostId", instance.Host.UniqueId);
            Set(wire.Attributes, "HostName", SafeName(instance.Host));
            Set(wire.Attributes, "HostCategory",
                instance.Host.Category == null ? null : instance.Host.Category.Name);
        }

        /// <summary>Copies the computed quantities the semantic layer reads.</summary>
        /// <remarks>
        /// These are parameter values Revit has already computed, not geometry. Reading them costs
        /// a parameter lookup; deriving the same numbers from solids would mean tessellating the
        /// model, which is explicitly out of scope here.
        /// </remarks>
        private static void AddQuantities(Element element, BridgeElement wire)
        {
            AddParameter(element, BuiltInParameter.HOST_AREA_COMPUTED, "Area", wire);
            AddParameter(element, BuiltInParameter.HOST_VOLUME_COMPUTED, "Volume", wire);
            AddParameter(element, BuiltInParameter.CURVE_ELEM_LENGTH, "Length", wire);
            AddParameter(element, BuiltInParameter.LEVEL_ELEV, "Elevation", wire);
        }

        private static void AddParameter(
            Element element,
            BuiltInParameter builtIn,
            string name,
            BridgeElement wire)
        {
            Parameter parameter = element.get_Parameter(builtIn);

            if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.Double)
            {
                return;
            }

            // Invariant culture, because the value is parsed on the other side of the bridge by a
            // process with its own locale. A comma decimal separator here becomes a silent
            // ten-thousandfold error there.
            Set(wire.Attributes, name, parameter.AsDouble().ToString("R", CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// The categories whose geometry this slice extracts.
        /// </summary>
        /// <remarks>
        /// Built-in category identifiers rather than names, because names are localised and a model
        /// authored in a non-English Revit would match none of them.
        /// </remarks>
        private static readonly HashSet<BuiltInCategory> GeometryCategories = new HashSet<BuiltInCategory>
        {
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_GenericModel,
        };

        /// <summary>Attaches the element's plan footprint, when its category is in scope.</summary>
        /// <remarks>
        /// A failure here is recorded and the element still travels. Geometry is an enrichment of an
        /// element that already carries identity, classification and parameters; dropping the whole
        /// element because its outline could not be read would lose more than it saved.
        /// </remarks>
        private static void AddGeometry(
            Element element,
            RevitGeometryExtractor extractor,
            BridgeDocument wire,
            BridgeElement target)
        {
            if (!IsGeometryCategory(element))
            {
                return;
            }

            string kind;
            string warning;
            string wkt;

            try
            {
                wkt = extractor.Extract(element, out kind, out warning);
            }
            catch (Exception exception)
            {
                wire.Warnings.Add(
                    "Element " + element.Id.ToString() + " geometry failed: " + exception.Message);
                return;
            }

            if (warning != null)
            {
                wire.Warnings.Add("Element " + element.Id.ToString() + ": " + warning);
            }

            if (wkt == null)
            {
                return;
            }

            target.GeometryKind = kind;
            target.GeometryWkt = wkt;
        }

        /// <summary>
        /// The supported categories as element ids, built once for comparison against a live one.
        /// </summary>
        /// <remarks>
        /// Comparing <c>ElementId</c> values sidesteps extracting an integer from one
        /// altogether. <c>ElementId.IntegerValue</c> is deprecated in the 2024 API, and parsing what
        /// <c>ToString</c> returns would be relying on a format nobody promised.
        /// </remarks>
        private static readonly HashSet<ElementId> GeometryCategoryIds = BuildGeometryCategoryIds();

        private static HashSet<ElementId> BuildGeometryCategoryIds()
        {
            HashSet<ElementId> ids = new HashSet<ElementId>();

            foreach (BuiltInCategory category in GeometryCategories)
            {
                ids.Add(new ElementId(category));
            }

            return ids;
        }

        private static bool IsGeometryCategory(Element element)
        {
            return element != null
                && element.Category != null
                && GeometryCategoryIds.Contains(element.Category.Id);
        }

        /// <summary>Reads an element's name, which is not a property every element supports.</summary>
        /// <remarks>
        /// <c>Element.Name</c> throws for element types that have no name rather than returning
        /// empty. One such element in a model would otherwise abandon the entire read.
        /// </remarks>
        /// <param name="element">The element to name.</param>
        /// <returns>The name, or null when the element has none.</returns>
        private static string SafeName(Element element)
        {
            try
            {
                return element.Name;
            }
            catch (Autodesk.Revit.Exceptions.InvalidOperationException)
            {
                return null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }

        private static void Set(IDictionary<string, string> target, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                target[key] = value;
            }
        }
    }
}
