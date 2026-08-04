using System.Collections.Generic;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Ifc;

internal sealed class IfcCapabilities : IPluginCapabilities
{
    public IReadOnlyList<string> SupportedFormats { get; } = new[] { ".ifc", ".ifczip" };
    public IReadOnlyList<string> SupportedGeometryTypes { get; } = new[] { "Mesh", "Brep", "Curve" };
    public IReadOnlyList<string> SupportedSemanticObjects { get; } = new[] { "IfcWall", "IfcDoor", "IfcWindow", "IfcSpace", "IfcBuildingStorey", "IfcSlab", "IfcColumn", "IfcBeam" };
    public IReadOnlyList<string> SupportedAttributes { get; } = new[] { "GlobalId", "Name", "ObjectType", "PredefinedType", "BuildingStorey", "Material" };
    public IReadOnlyList<string> SupportedCRS { get; } = new[] { "Local Placement", "Project Coordinate" };
    public IReadOnlyList<string> SupportedQaRules { get; } = new[] { "Doors hosted by walls", "Rooms closed", "Missing levels" };
}
