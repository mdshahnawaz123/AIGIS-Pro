namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// Owns the plugin lifecycle: discover, load, unload.
/// </summary>
public interface IPluginHost
{
    /// <summary>Gets every discovered plugin, loaded or not.</summary>
    IReadOnlyList<PluginDescriptor> Plugins { get; }

    /// <summary>Discovers and loads every eligible plugin.</summary>
    /// <param name="cancellationToken">Token used to cancel loading.</param>
    /// <returns>The descriptors, with their resulting state.</returns>
    Task<IReadOnlyList<PluginDescriptor>> LoadAllAsync(CancellationToken cancellationToken = default);

    /// <summary>Unloads a single plugin and removes its capabilities.</summary>
    /// <param name="pluginId">The plugin to unload.</param>
    /// <param name="cancellationToken">Token used to cancel the unload.</param>
    /// <returns><see langword="true"/> when the plugin was loaded and has been released.</returns>
    Task<bool> UnloadAsync(string pluginId, CancellationToken cancellationToken = default);

    /// <summary>Unloads every plugin. Called on host shutdown.</summary>
    /// <param name="cancellationToken">Token used to cancel the unload.</param>
    /// <returns>A task that completes when every plugin has been released.</returns>
    Task UnloadAllAsync(CancellationToken cancellationToken = default);
}
