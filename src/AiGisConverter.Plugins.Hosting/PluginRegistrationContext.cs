using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// Default <see cref="IPluginRegistrationContext"/>. Collects the plugin's service registrations
/// and forwards its capability registrations to the host registry.
/// </summary>
/// <remarks>
/// The plugin's services are built into a provider of their own rather than being merged into the
/// host container. That containment is what makes unloading possible: disposing one provider
/// releases every object the plugin created, and nothing in the host container holds a reference
/// to a type from the plugin's load context.
/// </remarks>
internal sealed class PluginRegistrationContext : IPluginRegistrationContext, IAsyncDisposable
{
    private readonly CapabilityRegistry _registry;
    private readonly List<(Type Contract, Func<IServiceProvider, object> Factory)> _pending = [];
    private readonly List<(Type Contract, object Instance)> _instances = [];
    private ServiceProvider? _provider;

    public PluginRegistrationContext(IPluginContext context, CapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(registry);

        Context = context;
        _registry = registry;

        Services = new ServiceCollection();
        Services.AddSingleton(context);
        Services.AddSingleton(context.Logger);
        Services.AddSingleton(context.Configuration);
    }

    public IPluginContext Context { get; }

    public IServiceCollection Services { get; }

    public IPluginRegistrationContext AddCapability<TContract>(TContract instance)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(instance);
        _instances.Add((typeof(TContract), instance));

        return this;
    }

    public IPluginRegistrationContext AddCapability<TContract>(Func<IServiceProvider, TContract> factory)
        where TContract : class
    {
        ArgumentNullException.ThrowIfNull(factory);
        _pending.Add((typeof(TContract), sp => factory(sp)));

        return this;
    }

    public IPluginRegistrationContext AddCapability<TContract, TImplementation>()
        where TContract : class
        where TImplementation : class, TContract
    {
        Services.TryAddPluginImplementation<TImplementation>();
        _pending.Add((typeof(TContract), static sp => sp.GetRequiredService<TImplementation>()));

        return this;
    }

    /// <summary>
    /// Builds the plugin's service provider and publishes its capabilities. Called by the loader
    /// after <see cref="IPlugin.ConfigureAsync"/> returns.
    /// </summary>
    /// <param name="pluginId">The contributing plugin.</param>
    /// <param name="loadOrder">The plugin's load order.</param>
    public void Publish(string pluginId, int loadOrder)
    {
        _provider = Services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = false,
            ValidateScopes = true,
        });

        foreach ((Type contract, object instance) in _instances)
        {
            _registry.Register(contract, pluginId, loadOrder, () => instance);
        }

        foreach ((Type contract, Func<IServiceProvider, object> factory) in _pending)
        {
            ServiceProvider provider = _provider;
            _registry.Register(contract, pluginId, loadOrder, () => factory(provider));
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_provider is not null)
        {
            await _provider.DisposeAsync().ConfigureAwait(false);
            _provider = null;
        }

        _pending.Clear();
        _instances.Clear();
    }
}

/// <summary>Small helper keeping the registration context readable.</summary>
internal static class PluginServiceCollectionHelpers
{
    /// <summary>Registers a plugin implementation type as a singleton if it is not already present.</summary>
    /// <typeparam name="TImplementation">The implementation type.</typeparam>
    /// <param name="services">The plugin's service collection.</param>
    public static void TryAddPluginImplementation<TImplementation>(this IServiceCollection services)
        where TImplementation : class
    {
        if (services.All(descriptor => descriptor.ServiceType != typeof(TImplementation)))
        {
            services.AddSingleton<TImplementation>();
        }
    }
}
