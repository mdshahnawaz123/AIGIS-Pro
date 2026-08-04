using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Plugins.Abstractions;
using AiGisConverter.Plugins.Hosting;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Composition;

/// <summary>
/// Runs plugin loading during application start-up and tells the layers that cache capability
/// lookups to re-read them.
/// </summary>
/// <remarks>
/// Ordering matters and is easy to get silently wrong: the AI provider index must be invalidated
/// <em>after</em> plugins have loaded, or a plugin-contributed provider stays invisible until the
/// application is restarted. Putting that dependency in one named class rather than in start-up
/// code makes it something you can point at and test.
/// </remarks>
public sealed class PluginBootstrapper
{
    private readonly IPluginHost _pluginHost;
    private readonly IAIProviderFactory _aiProviderFactory;
    private readonly ILogger<PluginBootstrapper> _logger;

    /// <summary>Initializes a new instance of the <see cref="PluginBootstrapper"/> class.</summary>
    /// <param name="pluginHost">The plugin host.</param>
    /// <param name="aiProviderFactory">The AI provider factory, whose index must be refreshed.</param>
    /// <param name="logger">Logger for start-up diagnostics.</param>
    public PluginBootstrapper(
        IPluginHost pluginHost,
        IAIProviderFactory aiProviderFactory,
        ILogger<PluginBootstrapper> logger)
    {
        ArgumentNullException.ThrowIfNull(pluginHost);
        ArgumentNullException.ThrowIfNull(aiProviderFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _pluginHost = pluginHost;
        _aiProviderFactory = aiProviderFactory;
        _logger = logger;
    }

    /// <summary>Loads every eligible plugin and refreshes dependent caches.</summary>
    /// <param name="cancellationToken">Token used to cancel start-up.</param>
    /// <returns>The descriptors, with their resulting state.</returns>
    public async Task<IReadOnlyList<PluginDescriptor>> StartAsync(CancellationToken cancellationToken = default)
    {
        IReadOnlyList<PluginDescriptor> descriptors =
            await _pluginHost.LoadAllAsync(cancellationToken).ConfigureAwait(false);

        _aiProviderFactory.Refresh();

        foreach (PluginDescriptor descriptor in descriptors.Where(static d => d.State is not PluginLoadState.Loaded))
        {
            _logger.LogInformation(
                "Plugin {PluginId} was not loaded ({State}): {Reason}",
                descriptor.Id,
                descriptor.State,
                descriptor.FailureReason ?? "no reason recorded");
        }

        return descriptors;
    }

    /// <summary>Shuts every plugin down.</summary>
    /// <param name="cancellationToken">Token used to bound shutdown.</param>
    /// <returns>A task that completes when every plugin has been released.</returns>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _pluginHost.UnloadAllAsync(cancellationToken).ConfigureAwait(false);
        _aiProviderFactory.Refresh();
    }
}
