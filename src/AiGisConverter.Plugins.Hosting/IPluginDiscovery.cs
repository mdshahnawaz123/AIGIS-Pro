namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// Finds plugins on disk and validates their manifests, without loading any assembly.
/// </summary>
public interface IPluginDiscovery
{
    /// <summary>Scans the configured search paths.</summary>
    /// <param name="cancellationToken">Token used to cancel the scan.</param>
    /// <returns>
    /// Every plugin folder found, including those rejected. Rejected entries are returned rather
    /// than dropped so the plugin manager can show the user why something did not appear.
    /// </returns>
    Task<IReadOnlyList<PluginDescriptor>> DiscoverAsync(CancellationToken cancellationToken = default);
}
