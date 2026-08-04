using AiGisConverter.Plugins.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Plugins.Hosting.DependencyInjection;

/// <summary>
/// Composition-root entry point for the plugin system.
/// </summary>
public static class PluginsServiceCollectionExtensions
{
    /// <summary>
    /// Registers plugin discovery, the host and the capability registry. Plugins are not loaded
    /// here: call <see cref="IPluginHost.LoadAllAsync"/> during application start-up, once the
    /// rest of the container is built.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>Plugins</c> section.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPluginSystem(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<PluginOptions>()
            .Bind(configuration.GetSection(PluginOptions.SectionName));

        services.TryAddSingleton<CapabilityRegistry>();
        services.TryAddSingleton<ICapabilityRegistry>(static sp => sp.GetRequiredService<CapabilityRegistry>());
        services.TryAddSingleton<IPluginDiscovery, PluginDiscovery>();

        services.TryAddSingleton<IPluginHost>(static sp => new PluginHost(
            sp.GetRequiredService<IPluginDiscovery>(),
            sp.GetRequiredService<CapabilityRegistry>(),
            sp.GetRequiredService<IOptionsMonitor<PluginOptions>>(),
            sp.GetRequiredService<IConfiguration>(),
            sp.GetRequiredService<ILoggerFactory>(),
            sp.GetRequiredService<ILogger<PluginHost>>()));

        return services;
    }
}
