namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// The entry point every plugin implements. One per plugin assembly.
/// </summary>
/// <remarks>
/// <para>
/// The lifecycle is deliberately three-phase. <see cref="ConfigureAsync"/> may do real work &#8212;
/// probing for an installed CAD application, opening a licence, connecting a bridge &#8212; and may
/// decline to register anything if its prerequisites are absent. A plugin that cannot function
/// should register no capabilities and log why, rather than throwing: one missing vendor SDK must
/// not prevent the application from starting.
/// </para>
/// <para>
/// <see cref="ShutdownAsync"/> must release every native handle, pipe and file lock the plugin
/// holds. Anything left behind pins the plugin's <c>AssemblyLoadContext</c> and defeats unloading.
/// </para>
/// </remarks>
public interface IPlugin
{
    /// <summary>Gets the plugin identifier, which must match the manifest's <c>id</c>.</summary>
    string Id { get; }

    /// <summary>
    /// Registers the plugin's capabilities. Called once, after the assembly is loaded and before
    /// the host resolves any capability.
    /// </summary>
    /// <param name="registration">The registration surface.</param>
    /// <param name="cancellationToken">Token used to cancel loading.</param>
    /// <returns>A task that completes when registration is finished.</returns>
    Task ConfigureAsync(IPluginRegistrationContext registration, CancellationToken cancellationToken = default);

    /// <summary>
    /// Releases everything the plugin holds. Called before the load context is unloaded, and on
    /// host shutdown.
    /// </summary>
    /// <param name="cancellationToken">Token used to bound the shutdown.</param>
    /// <returns>A task that completes when the plugin has released its resources.</returns>
    Task ShutdownAsync(CancellationToken cancellationToken = default);
}
