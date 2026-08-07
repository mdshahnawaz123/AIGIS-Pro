using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using AiGisConverter.Bridge.Protocol;

namespace AiGisConverter.Addin.Revit
{
    /// <summary>
    /// Reads an element's BIM identity, relationships and parameters into the wire format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// One instance per document read, because it holds the type cache. A model of twenty thousand
    /// elements typically has a few hundred types, and reading a type's parameters once per
    /// instance rather than once per type is the difference between an export that finishes and one
    /// that appears to hang.
    /// </para>
    /// <para>
    /// Every read here is defensive. Revit throws from property getters for reasons that are
    /// specific to one element in one model - a room that belongs to a phase this element does not,
    /// a workset table on a document that is not workshared, a parameter whose storage type does
    /// not match its declared data type. None of those is a reason to lose the element, so each is
    /// caught where it happens and the remaining attributes are written anyway.
    /// </para>
    /// </remarks>
    internal sealed class RevitSemantics
    {
        /// <summary>The most parameters written for one element, instance and type combined.</summary>
        /// <remarks>
        /// A bound rather than a judgement. Some families carry hundreds of parameters, and an
        /// export whose attribute payload dwarfs its geometry helps nobody; the count of what was
        /// dropped is recorded so the limit is visible rather than silent.
        /// </remarks>
        internal const int MaximumParametersPerElement = 200;

        private readonly Document _document;
        private readonly Dictionary<string, Dictionary<string, string>> _typeAttributes =
            new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        private readonly Dictionary<string, int> _skipped =
            new Dictionary<string, int>(StringComparer.Ordinal);

        private WorksetTable _worksets;
        private bool _worksetsResolved;

        /// <summary>Initializes a new instance of the <see cref="RevitSemantics"/> class.</summary>
        /// <param name="document">The document being read.</param>
        internal RevitSemantics(Document document)
        {
            if (document == null)
            {
                throw new ArgumentNullException(nameof(document));
            }

            _document = document;
        }

        /// <summary>Gets how many elements reused a cached type.</summary>
        internal int TypeCacheHits { get; private set; }

        /// <summary>Gets how many distinct types were read.</summary>
        internal int TypesRead
        {
            get { return _typeAttributes.Count; }
        }

        /// <summary>Gets how many parameter values were written.</summary>
        internal int ParametersWritten { get; private set; }

        /// <summary>Gets how many parameters were dropped, by reason.</summary>
        /// <remarks>
        /// Named by reason rather than counted in one total, because "unsupported storage type" and
        /// "threw on read" call for different responses and only one of them is expected.
        /// </remarks>
        internal IDictionary<string, int> Skipped
        {
            get { return _skipped; }
        }

        /// <summary>Writes every semantic attribute available for an element.</summary>
        /// <param name="element">The element to read.</param>
        /// <param name="wire">The wire element to enrich.</param>
        internal void Enrich(Element element, BridgeElement wire)
        {
            if (element == null || wire == null)
            {
                return;
            }

            AddIdentity(element, wire);
            AddPhaseWorksetAndOption(element, wire);
            AddNamedParameters(element, wire);
            AddMaterials(element, wire);
            AddRelationships(element, wire);
            AddGeometryMetadata(element, wire);
            AddAllParameters(element, wire);
        }

        // ------------------------------------------------------------------ identity

        private void AddIdentity(Element element, BridgeElement wire)
        {
            // The feature id already carries this, but a consumer reading the attribute table alone
            // should not have to know that. It is the only identifier stable across save and reload.
            Set(wire, "UniqueId", element.UniqueId);
        }

        private void AddPhaseWorksetAndOption(Element element, BridgeElement wire)
        {
            Set(wire, "Phase", ElementIdName(ParameterElementId(element, BuiltInParameter.PHASE_CREATED)));
            Set(wire, "PhaseDemolished", ElementIdName(ParameterElementId(element, BuiltInParameter.PHASE_DEMOLISHED)));
            Set(wire, "Workset", WorksetName(element));

            try
            {
                DesignOption option = element.DesignOption;

                if (option != null)
                {
                    Set(wire, "DesignOption", option.Name);
                    Set(wire, "DesignOptionId", option.UniqueId);
                }
            }
            catch (Exception exception)
            {
                Skip("DesignOption:" + exception.GetType().Name);
            }
        }

        private string WorksetName(Element element)
        {
            if (!_worksetsResolved)
            {
                _worksetsResolved = true;

                try
                {
                    // Only a workshared document has a workset table; asking an unshared one throws
                    // once per element otherwise.
                    _worksets = _document.IsWorkshared ? _document.GetWorksetTable() : null;
                }
                catch (Exception exception)
                {
                    Skip("WorksetTable:" + exception.GetType().Name);
                    _worksets = null;
                }
            }

            if (_worksets == null)
            {
                return null;
            }

            try
            {
                Workset workset = _worksets.GetWorkset(element.WorksetId);

                return workset == null ? null : workset.Name;
            }
            catch (Exception exception)
            {
                Skip("Workset:" + exception.GetType().Name);
                return null;
            }
        }

        // ------------------------------------------------------------------ named parameters

        private void AddNamedParameters(Element element, BridgeElement wire)
        {
            // Read by BuiltInParameter, never by name: parameter names are localised, and this
            // model is authored in Chinese. A name lookup would find none of these.
            Set(wire, "Mark", ParameterText(element, BuiltInParameter.ALL_MODEL_MARK));
            Set(wire, "Comments", ParameterText(element, BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));

            ElementType type = TypeOf(element);

            if (type == null)
            {
                return;
            }

            // Exactly the fields that were asked for by name, and no more. Manufacturer, model,
            // assembly description and the rest all reach the export anyway, through the parameter
            // dump below - which is the point of dumping them. Naming a field here buys a stable
            // key and costs a BuiltInParameter member that has to exist, so the trade is only worth
            // making for keys a consumer will look for by name.
            Set(wire, "Description", ParameterText(type, BuiltInParameter.ALL_MODEL_DESCRIPTION));
            Set(wire, "TypeMark", ParameterText(type, BuiltInParameter.ALL_MODEL_TYPE_MARK));
            Set(wire, "AssemblyCode", ParameterText(type, BuiltInParameter.UNIFORMAT_CODE));
            Set(wire, "OmniClassNumber", ParameterText(type, BuiltInParameter.OMNICLASS_CODE));
        }

        // ------------------------------------------------------------------ materials

        private void AddMaterials(Element element, BridgeElement wire)
        {
            ICollection<ElementId> materialIds;

            try
            {
                materialIds = element.GetMaterialIds(false);
            }
            catch (Exception exception)
            {
                Skip("Materials:" + exception.GetType().Name);
                return;
            }

            if (materialIds == null || materialIds.Count == 0)
            {
                return;
            }

            List<string> names = new List<string>();
            List<string> ids = new List<string>();

            foreach (ElementId materialId in materialIds)
            {
                Material material = _document.GetElement(materialId) as Material;

                if (material == null)
                {
                    continue;
                }

                names.Add(material.Name);
                ids.Add(material.UniqueId);
            }

            // Joined rather than indexed. An element with three materials would otherwise add three
            // columns that are empty on every element with one, and a schema that widens with its
            // widest row is not one a GIS consumer can append to.
            Set(wire, "Material", BimNaming.Join(names));
            Set(wire, "MaterialId", BimNaming.Join(ids));
            Set(wire, "MaterialCount", BimNaming.Integer(names.Count));
        }

        // ------------------------------------------------------------------ relationships

        private void AddRelationships(Element element, BridgeElement wire)
        {
            try
            {
                if (element.GroupId != null && element.GroupId != ElementId.InvalidElementId)
                {
                    Element group = _document.GetElement(element.GroupId);

                    if (group != null)
                    {
                        Set(wire, "GroupId", group.UniqueId);
                        Set(wire, "GroupName", SafeName(group));
                    }
                }
            }
            catch (Exception exception)
            {
                Skip("Group:" + exception.GetType().Name);
            }

            FamilyInstance instance = element as FamilyInstance;

            if (instance == null)
            {
                return;
            }

            try
            {
                Element parent = instance.SuperComponent;

                if (parent != null)
                {
                    Set(wire, "ParentId", parent.UniqueId);
                    Set(wire, "ParentName", SafeName(parent));
                }
            }
            catch (Exception exception)
            {
                Skip("SuperComponent:" + exception.GetType().Name);
            }

            AddSpatialContainer(instance, wire);
        }

        private void AddSpatialContainer(FamilyInstance instance, BridgeElement wire)
        {
            // Both getters resolve against the document's last phase and throw when the element does
            // not exist in it. Common in a phased model and not an error.
            try
            {
                Element room = instance.Room;

                if (room != null)
                {
                    Set(wire, "RoomId", room.UniqueId);
                    Set(wire, "RoomName", SafeName(room));
                    Set(wire, "RoomNumber", ParameterText(room, BuiltInParameter.ROOM_NUMBER));
                }
            }
            catch (Exception exception)
            {
                Skip("Room:" + exception.GetType().Name);
            }

            try
            {
                Element space = instance.Space;

                if (space != null)
                {
                    Set(wire, "SpaceId", space.UniqueId);
                    Set(wire, "SpaceName", SafeName(space));
                }
            }
            catch (Exception exception)
            {
                Skip("Space:" + exception.GetType().Name);
            }
        }

        // ------------------------------------------------------------------ geometry metadata

        /// <summary>Writes measurable facts about the element's extent.</summary>
        /// <remarks>
        /// Height, width and thickness have no universal parameter in Revit. A wall's thickness, a
        /// floor's thickness and a door's width are three different <c>BuiltInParameter</c> members
        /// on three different categories, and picking one to stand for all of them produces a
        /// column that is populated for furniture and empty for the building. So the generic
        /// answers come from the bounding box, named for what they are, and the one authored
        /// dimension that generalises - the thickness of a layered construction - is read through
        /// the single API that covers walls, floors, roofs and ceilings alike. Authored values for
        /// everything else arrive through the parameter dump under their real names.
        /// </remarks>
        private void AddGeometryMetadata(Element element, BridgeElement wire)
        {
            AddBoundingBox(element, wire);
            AddThickness(element, wire);
            AddRotation(element, wire);
        }

        private void AddThickness(Element element, BridgeElement wire)
        {
            try
            {
                HostObjAttributes host = TypeOf(element) as HostObjAttributes;

                if (host == null)
                {
                    return;
                }

                CompoundStructure structure = host.GetCompoundStructure();

                if (structure == null)
                {
                    return;
                }

                Set(wire, "Thickness", BimNaming.Number(structure.GetWidth() * Footprint.MetresPerFoot));
            }
            catch (Exception exception)
            {
                Skip("Thickness:" + exception.GetType().Name);
            }
        }

        private void AddBoundingBox(Element element, BridgeElement wire)
        {
            BoundingBoxXYZ box;

            try
            {
                box = element.get_BoundingBox(null);
            }
            catch (Exception exception)
            {
                Skip("BoundingBox:" + exception.GetType().Name);
                return;
            }

            if (box == null || box.Min == null || box.Max == null)
            {
                return;
            }

            // Metres, matching the geometry. Feet in the attributes beside metres in the geometry is
            // the kind of inconsistency nobody notices until a height is plotted against a footprint.
            Set(wire, "BoundsMinX", BimNaming.Number(box.Min.X * Footprint.MetresPerFoot));
            Set(wire, "BoundsMinY", BimNaming.Number(box.Min.Y * Footprint.MetresPerFoot));
            Set(wire, "BoundsMinZ", BimNaming.Number(box.Min.Z * Footprint.MetresPerFoot));
            Set(wire, "BoundsMaxX", BimNaming.Number(box.Max.X * Footprint.MetresPerFoot));
            Set(wire, "BoundsMaxY", BimNaming.Number(box.Max.Y * Footprint.MetresPerFoot));
            Set(wire, "BoundsMaxZ", BimNaming.Number(box.Max.Z * Footprint.MetresPerFoot));

            // Named Bounds rather than Width and Height, because that is what they are: the box is
            // axis-aligned to the project, so for a rotated element these are the extents of its
            // envelope and not the dimensions of the element. Calling them Width and Height would
            // be right for most of a model and quietly wrong for the rest.
            Set(wire, "BoundsWidth", BimNaming.Number((box.Max.X - box.Min.X) * Footprint.MetresPerFoot));
            Set(wire, "BoundsDepth", BimNaming.Number((box.Max.Y - box.Min.Y) * Footprint.MetresPerFoot));
            Set(wire, "BoundsHeight", BimNaming.Number((box.Max.Z - box.Min.Z) * Footprint.MetresPerFoot));
        }

        private void AddRotation(Element element, BridgeElement wire)
        {
            try
            {
                LocationPoint point = element.Location as LocationPoint;

                if (point == null)
                {
                    return;
                }

                // Radians, as Revit holds it. Degrees would be friendlier and would also be a second
                // convention for the same quantity; the unit is named in the key's documentation.
                Set(wire, "RotationRadians", BimNaming.Number(point.Rotation));
            }
            catch (Exception exception)
            {
                // A LocationPoint whose element has no rotation throws rather than returning zero.
                Skip("Rotation:" + exception.GetType().Name);
            }
        }

        // ------------------------------------------------------------------ all parameters

        private void AddAllParameters(Element element, BridgeElement wire)
        {
            int written = WriteParameters(element, wire, BimNaming.InstancePrefix);

            ElementType type = TypeOf(element);

            if (type == null)
            {
                return;
            }

            Dictionary<string, string> cached = TypeAttributes(type);

            foreach (KeyValuePair<string, string> pair in cached)
            {
                if (written >= MaximumParametersPerElement)
                {
                    Skip("ParameterLimitReached");
                    break;
                }

                if (!wire.Attributes.ContainsKey(pair.Key))
                {
                    wire.Attributes[pair.Key] = pair.Value;
                    written++;
                    ParametersWritten++;
                }
            }
        }

        /// <summary>Reads a type's parameters once and remembers them.</summary>
        private Dictionary<string, string> TypeAttributes(ElementType type)
        {
            string key = type.Id.ToString();
            Dictionary<string, string> cached;

            if (_typeAttributes.TryGetValue(key, out cached))
            {
                TypeCacheHits++;
                return cached;
            }

            cached = new Dictionary<string, string>(StringComparer.Ordinal);

            BridgeElement scratch = new BridgeElement();

            WriteParameters(type, scratch, BimNaming.TypePrefix);

            foreach (KeyValuePair<string, string> pair in scratch.Attributes)
            {
                cached[pair.Key] = pair.Value;
            }

            _typeAttributes[key] = cached;

            return cached;
        }

        private int WriteParameters(Element element, BridgeElement wire, string prefix)
        {
            int written = 0;

            ParameterSet parameters;

            try
            {
                parameters = element.Parameters;
            }
            catch (Exception exception)
            {
                Skip("ParameterSet:" + exception.GetType().Name);
                return written;
            }

            if (parameters == null)
            {
                return written;
            }

            foreach (Parameter parameter in parameters)
            {
                if (written >= MaximumParametersPerElement)
                {
                    Skip("ParameterLimitReached");
                    break;
                }

                string key;
                string value;

                if (!TryReadParameter(parameter, prefix, out key, out value))
                {
                    continue;
                }

                // First writer wins. The named attributes above are written before this runs, so a
                // user parameter can never displace Category, Level or a geometry field - and the
                // prefix means it could not collide with one in the first place.
                if (!wire.Attributes.ContainsKey(key))
                {
                    wire.Attributes[key] = value;
                    written++;
                    ParametersWritten++;
                }
            }

            return written;
        }

        private bool TryReadParameter(Parameter parameter, string prefix, out string key, out string value)
        {
            key = null;
            value = null;

            if (parameter == null)
            {
                return false;
            }

            try
            {
                if (!parameter.HasValue)
                {
                    return false;
                }

                Definition definition = parameter.Definition;

                if (definition == null)
                {
                    Skip("NoDefinition");
                    return false;
                }

                key = prefix == BimNaming.TypePrefix
                    ? BimNaming.TypeKey(definition.Name)
                    : BimNaming.InstanceKey(definition.Name);

                if (key == null)
                {
                    Skip("UnusableName");
                    return false;
                }

                value = ReadValue(parameter, definition);

                if (string.IsNullOrEmpty(value))
                {
                    return false;
                }

                value = BimNaming.Truncate(value);

                return true;
            }
            catch (Exception exception)
            {
                // Continue on exception, per the export contract. A parameter that throws is one
                // attribute lost, not one element lost.
                Skip("Read:" + exception.GetType().Name);
                return false;
            }
        }

        private string ReadValue(Parameter parameter, Definition definition)
        {
            switch (parameter.StorageType)
            {
                case StorageType.String:
                    return parameter.AsString();

                case StorageType.Integer:
                    return IsYesNo(definition)
                        ? BimNaming.YesNo(parameter.AsInteger())
                        : BimNaming.Integer(parameter.AsInteger());

                case StorageType.Double:
                    return BimNaming.Number(parameter.AsDouble());

                case StorageType.ElementId:
                    return ElementIdName(parameter.AsElementId());

                default:
                    // StorageType.None, and anything a later API adds. Skipped rather than guessed.
                    Skip("StorageType:" + parameter.StorageType.ToString());
                    return null;
            }
        }

        private static bool IsYesNo(Definition definition)
        {
            try
            {
                return definition.GetDataType() == SpecTypeId.Boolean.YesNo;
            }
            catch (Exception)
            {
                // A definition whose data type cannot be read is treated as a plain integer, which
                // is what it is stored as.
                return false;
            }
        }

        // ------------------------------------------------------------------ helpers

        private ElementType TypeOf(Element element)
        {
            try
            {
                ElementId typeId = element.GetTypeId();

                if (typeId == null || typeId == ElementId.InvalidElementId)
                {
                    return null;
                }

                return _document.GetElement(typeId) as ElementType;
            }
            catch (Exception exception)
            {
                Skip("TypeOf:" + exception.GetType().Name);
                return null;
            }
        }

        private ElementId ParameterElementId(Element element, BuiltInParameter builtIn)
        {
            try
            {
                Parameter parameter = element.get_Parameter(builtIn);

                if (parameter == null || !parameter.HasValue || parameter.StorageType != StorageType.ElementId)
                {
                    return null;
                }

                return parameter.AsElementId();
            }
            catch (Exception exception)
            {
                Skip("ParameterElementId:" + exception.GetType().Name);
                return null;
            }
        }

        private string ElementIdName(ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId)
            {
                return null;
            }

            try
            {
                Element referenced = _document.GetElement(id);

                // Resolved to a name where one exists, and left as the raw id where it does not -
                // a built-in category id is negative and resolves to nothing, and printing it is
                // more use than printing an empty string.
                return referenced == null ? id.ToString() : (SafeName(referenced) ?? id.ToString());
            }
            catch (Exception exception)
            {
                Skip("ElementIdName:" + exception.GetType().Name);
                return null;
            }
        }

        private string ParameterText(Element element, BuiltInParameter builtIn)
        {
            try
            {
                Parameter parameter = element.get_Parameter(builtIn);

                if (parameter == null || !parameter.HasValue)
                {
                    return null;
                }

                return parameter.StorageType == StorageType.String
                    ? parameter.AsString()
                    : parameter.AsValueString();
            }
            catch (Exception exception)
            {
                Skip("ParameterText:" + exception.GetType().Name);
                return null;
            }
        }

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

        private void Skip(string reason)
        {
            int already;

            _skipped[reason] = _skipped.TryGetValue(reason, out already) ? already + 1 : 1;
        }

        private static void Set(BridgeElement wire, string key, string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                wire.Attributes[key] = value;
            }
        }
    }
}
