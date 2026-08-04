using System.Collections.Generic;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// Exposes the capabilities supported by a plugin, allowing the host to dynamically discover 
/// what features a plugin can provide.
/// </summary>
public interface IPluginCapabilities
{
    /// <summary>Gets the file extensions or formats supported by this plugin.</summary>
    IReadOnlyList<string> SupportedFormats { get; }
    
    /// <summary>Gets the native geometry types supported.</summary>
    IReadOnlyList<string> SupportedGeometryTypes { get; }
    
    /// <summary>Gets the semantic object types (e.g. Revit Category, IFC Class) the plugin can extract.</summary>
    IReadOnlyList<string> SupportedSemanticObjects { get; }
    
    /// <summary>Gets the attribute fields the plugin natively reads.</summary>
    IReadOnlyList<string> SupportedAttributes { get; }
    
    /// <summary>Gets the Coordinate Reference Systems (CRS) the plugin supports.</summary>
    IReadOnlyList<string> SupportedCRS { get; }
    
    /// <summary>Gets the built-in QA/QC rules supported by this plugin.</summary>
    IReadOnlyList<string> SupportedQaRules { get; }
}
