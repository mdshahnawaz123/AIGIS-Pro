using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// What the host knows about a plugin: where it is, what it claims, and how it fared.
/// </summary>
public sealed class PluginDescriptor
{
    /// <summary>Initializes a new instance of the <see cref="PluginDescriptor"/> class.</summary>
    /// <param name="manifest">The parsed manifest.</param>
    /// <param name="directory">The folder the manifest was found in.</param>
    public PluginDescriptor(PluginManifest manifest, string directory)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        Manifest = manifest;
        Directory = directory;
    }

    /// <summary>Gets the parsed manifest.</summary>
    public PluginManifest Manifest { get; }

    /// <summary>Gets the folder the plugin was found in.</summary>
    public string Directory { get; }

    /// <summary>Gets the plugin identifier.</summary>
    public string Id => Manifest.Id;

    /// <summary>Gets or sets the lifecycle state.</summary>
    public PluginLoadState State { get; set; } = PluginLoadState.Discovered;

    /// <summary>Gets or sets why the plugin was rejected or failed. Null when it loaded cleanly.</summary>
    public string? FailureReason { get; set; }

    /// <summary>Gets or sets how long loading took.</summary>
    public TimeSpan LoadDuration { get; set; }

    /// <summary>Gets the full path to the entry assembly.</summary>
    /// <returns>The absolute path to the assembly named by the manifest.</returns>
    public string GetEntryAssemblyPath() => Path.Combine(Directory, Manifest.EntryAssembly);

    /// <inheritdoc />
    public override string ToString() => $"{Id} ({State})";
}
