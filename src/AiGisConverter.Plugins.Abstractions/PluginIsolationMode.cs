using System.Text.Json.Serialization;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// How a plugin's assemblies are loaded.
/// </summary>
/// <remarks>
/// <para>
/// The converter is part of the contract, not a serialisation detail. Every <c>plugin.json</c>
/// writes this as a name - <c>"isolation": "Isolated"</c> - because a manifest is meant to be
/// written and read by people, and an integer would make it neither.
/// </para>
/// <para>
/// Declared on the type so it holds for every reader and writer of a manifest. Without it,
/// <c>JsonSerializerDefaults.Web</c> deserialises enums from numbers only and throws on a name.
/// That failure is caught and logged as a skipped manifest, which meant no plugin was ever
/// discoverable and nothing said so - the whole plugin system was silently inert.
/// </para>
/// </remarks>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PluginIsolationMode
{
    /// <summary>
    /// The plugin is loaded into its own collectible <c>AssemblyLoadContext</c> with private
    /// dependency resolution. This is the default, and the reason two plugins can each carry an
    /// incompatible version of the same third-party library.
    /// </summary>
    Isolated = 0,

    /// <summary>
    /// The plugin is loaded into the host's default context. Reserved for first-party plugins that
    /// must share mutable static state with the host. Chosen deliberately, never by accident.
    /// </summary>
    Shared = 1,

    /// <summary>
    /// The plugin's real work happens in another process &#8212; typically an add-in running inside
    /// AutoCAD, Civil 3D or Revit &#8212; and the loaded assembly is only the client side of the
    /// bridge. See <see cref="PluginHostRequirement"/>.
    /// </summary>
    OutOfProcess = 2,
}
