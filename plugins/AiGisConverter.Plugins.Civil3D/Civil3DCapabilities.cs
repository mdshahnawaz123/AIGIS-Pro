using System.Collections.Generic;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Civil3D;

internal sealed class Civil3DCapabilities : IPluginCapabilities
{
    public IReadOnlyList<string> SupportedFormats { get; } = new[] { ".dwg", ".dxf" };
    public IReadOnlyList<string> SupportedGeometryTypes { get; } = new[] { "Mesh", "Curve", "Point", "Surface" };
    public IReadOnlyList<string> SupportedSemanticObjects { get; } = new[] { "Alignment", "Corridor", "Surface", "Pipe", "Structure", "Parcel", "FeatureLine" };
    public IReadOnlyList<string> SupportedAttributes { get; } = new[] { "Name", "Description", "Style", "Layer", "Station", "Offset", "Elevation", "Area", "Length", "Volume" };
    public IReadOnlyList<string> SupportedCRS { get; } = new[] { "WCS", "UCS", "Project Coordinate" };
    public IReadOnlyList<string> SupportedQaRules { get; } = new[] { "Pipes connected", "Buildings inside parcels", "Duplicate IDs" };
}
