namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// The host-side index of everything plugins have contributed, keyed by contract type.
/// </summary>
/// <remarks>
/// The application layer asks for <c>GetCapabilities&lt;IDataSourceReader&gt;()</c> and receives
/// every reader any plugin registered, without knowing a plugin system exists.
/// </remarks>
public interface ICapabilityRegistry
{
    /// <summary>Gets every capability registered against a contract.</summary>
    /// <typeparam name="TContract">The contract type.</typeparam>
    /// <returns>The registered capabilities, in plugin load order.</returns>
    IReadOnlyList<TContract> GetCapabilities<TContract>()
        where TContract : class;

    /// <summary>Gets every capability registered against a contract, with its owning plugin.</summary>
    /// <typeparam name="TContract">The contract type.</typeparam>
    /// <returns>The registered capabilities paired with the identifier of the plugin that supplied each.</returns>
    IReadOnlyList<(string PluginId, TContract Capability)> GetCapabilitiesWithSource<TContract>()
        where TContract : class;

    /// <summary>Gets the contract types that currently have at least one registration.</summary>
    /// <returns>The registered contract types.</returns>
    IReadOnlyList<Type> GetRegisteredContracts();
}
