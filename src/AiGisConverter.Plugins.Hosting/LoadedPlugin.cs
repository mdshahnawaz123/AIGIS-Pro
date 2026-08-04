using System.Runtime.Loader;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// A plugin that is currently loaded, together with everything needed to unload it again.
/// </summary>
internal sealed class LoadedPlugin : IAsyncDisposable
{
    public LoadedPlugin(
        PluginDescriptor descriptor,
        IPlugin instance,
        PluginRegistrationContext registration,
        AssemblyLoadContext? loadContext)
    {
        Descriptor = descriptor;
        Instance = instance;
        Registration = registration;
        LoadContext = loadContext;
    }

    public PluginDescriptor Descriptor { get; }

    public IPlugin Instance { get; }

    public PluginRegistrationContext Registration { get; }

    /// <summary>Gets the load context, or null when the plugin was loaded into the default context.</summary>
    public AssemblyLoadContext? LoadContext { get; }

    /// <summary>Shuts the plugin down and releases its services. Does not unload the context.</summary>
    public async ValueTask DisposeAsync()
    {
        using CancellationTokenSource cts = new(TimeSpan.FromSeconds(30));

        try
        {
            await Instance.ShutdownAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // The plugin did not shut down in time; proceed to release what the host controls.
        }

        await Registration.DisposeAsync().ConfigureAwait(false);
    }
}
