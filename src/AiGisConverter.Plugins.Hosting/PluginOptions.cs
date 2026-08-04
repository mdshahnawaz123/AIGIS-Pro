namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// Host-side plugin configuration, bound from the <c>Plugins</c> section.
/// </summary>
public sealed class PluginOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Plugins";

    /// <summary>
    /// Gets the folders scanned for plugins. Each direct child folder containing a
    /// <c>plugin.json</c> is treated as one plugin. Environment variables are expanded.
    /// </summary>
    public IList<string> SearchPaths { get; } =
    [
        "Plugins",
        "%LOCALAPPDATA%\\AiGisConverter\\Plugins",
    ];

    /// <summary>Gets the plugin identifiers to load. When non-empty, acts as an allowlist.</summary>
    public IList<string> Enabled { get; } = [];

    /// <summary>Gets the plugin identifiers to skip, applied after <see cref="Enabled"/>.</summary>
    public IList<string> Disabled { get; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether a plugin that fails to load aborts host startup.
    /// Left false: on an engineering workstation one broken vendor SDK must not prevent the
    /// application from opening at all.
    /// </summary>
    public bool FailFastOnLoadError { get; set; }

    /// <summary>Gets or sets the per-plugin load timeout in seconds.</summary>
    public int LoadTimeoutSeconds { get; set; } = 60;

    /// <summary>
    /// Gets the assembly simple names that must always resolve from the host rather than from a
    /// plugin folder. These are the contract assemblies: loading a second copy into a plugin's
    /// context would give it a distinct <see cref="Type"/> identity, and every cast across the
    /// boundary would fail with a message that makes no sense to anyone reading it.
    /// </summary>
    /// <remarks>A trailing <c>*</c> makes an entry a prefix match.</remarks>
    public IList<string> SharedAssemblies { get; } =
    [
        "AiGisConverter.Plugins.Abstractions",
        "AiGisConverter.Domain",
        "AiGisConverter.Ai",
        "AiGisConverter.Gis",
        "AiGisConverter.QaQc",
        "AiGisConverter.Bridge.Protocol",
        "NetTopologySuite",
        "Microsoft.Extensions.DependencyInjection.Abstractions",
        "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Configuration.Abstractions",
        "Microsoft.Extensions.Options",
        "Microsoft.Extensions.Primitives",
    ];
}
