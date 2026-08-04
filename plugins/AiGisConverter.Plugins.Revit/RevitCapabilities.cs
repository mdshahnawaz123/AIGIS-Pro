using System.Collections.Generic;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Revit;

internal sealed class RevitCapabilities : IPluginCapabilities
{
    public IReadOnlyList<string> SupportedFormats { get; } = new[] { ".rvt", ".rfa" };
    public IReadOnlyList<string> SupportedGeometryTypes { get; } = new[] { "Mesh", "Brep", "Curve" };
    public IReadOnlyList<string> SupportedSemanticObjects { get; } = new[] { "Wall", "Door", "Window", "Floor", "Roof", "Room", "Space", "Level", "Column", "Beam", "MEP" };
    public IReadOnlyList<string> SupportedAttributes { get; } = new[] { "Family", "Type", "Category", "Level", "Material", "Phase", "HostId", "LevelId" };
    public IReadOnlyList<string> SupportedCRS { get; } = new[] { "Project Base Point", "Survey Point", "Internal Origin" };
    public IReadOnlyList<string> SupportedQaRules { get; } = new[] { "Doors hosted by walls", "Rooms closed", "Missing levels" };
}
