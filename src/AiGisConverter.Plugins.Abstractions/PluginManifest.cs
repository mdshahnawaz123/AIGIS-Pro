using System.Text.Json.Serialization;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// The contents of a plugin's <c>plugin.json</c>.
/// </summary>
/// <remarks>
/// The manifest exists so the host can decide whether to load a plugin <em>without</em> loading it.
/// Compatibility, enablement and capability advertising are all answered from the manifest alone,
/// which means a broken or hostile assembly is never mapped into the process to find that out.
/// </remarks>
public sealed class PluginManifest
{
    /// <summary>The conventional manifest file name.</summary>
    public const string FileName = "plugin.json";

    /// <summary>Gets or sets the globally unique plugin identifier, for example <c>aigis.reader.ifc</c>.</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the display name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the plugin version.</summary>
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0.0";

    /// <summary>Gets or sets the SDK contract version the plugin was built against.</summary>
    [JsonPropertyName("sdkVersion")]
    public string SdkVersion { get; set; } = PluginSdk.Version;

    /// <summary>Gets or sets the publisher name.</summary>
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }

    /// <summary>Gets or sets a one-line description shown in the plugin manager.</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>Gets or sets the file name of the assembly containing the <see cref="IPlugin"/> type.</summary>
    [JsonPropertyName("entryAssembly")]
    public string EntryAssembly { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full type name of the <see cref="IPlugin"/> implementation. Optional:
    /// when omitted the loader scans the entry assembly for a single public implementation.
    /// </summary>
    [JsonPropertyName("entryType")]
    public string? EntryType { get; set; }

    /// <summary>Gets or sets the isolation mode.</summary>
    [JsonPropertyName("isolation")]
    public PluginIsolationMode Isolation { get; set; } = PluginIsolationMode.Isolated;

    /// <summary>
    /// Gets or sets the capability names this plugin advertises, for example
    /// <c>DataSourceReader</c> or <c>AIProvider</c>. Descriptive only: it lets the plugin manager
    /// group plugins without loading them. Binding is by type at registration time.
    /// </summary>
    [JsonPropertyName("capabilities")]
    public IList<string> Capabilities { get; set; } = [];

    /// <summary>Gets or sets the host application requirement, when the plugin has one.</summary>
    [JsonPropertyName("hostApplication")]
    public PluginHostRequirement? HostApplication { get; set; }

    /// <summary>Gets or sets a value indicating whether the plugin is enabled.</summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the load order. Lower values load first.</summary>
    [JsonPropertyName("loadOrder")]
    public int LoadOrder { get; set; } = 100;

    /// <summary>
    /// Gets or sets additional assembly name prefixes that must resolve from the host rather than
    /// from the plugin folder. Use when a plugin exchanges types with another plugin's contract.
    /// </summary>
    [JsonPropertyName("sharedAssemblies")]
    public IList<string> SharedAssemblies { get; set; } = [];

    /// <summary>Gets or sets the minimum host application version this plugin supports.</summary>
    [JsonPropertyName("minimumHostVersion")]
    public string? MinimumHostVersion { get; set; }

    /// <inheritdoc />
    public override string ToString() => $"{Id} {Version}";
}
