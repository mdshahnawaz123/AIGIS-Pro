using Microsoft.Extensions.DependencyInjection;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// The surface through which a plugin contributes capabilities to the host.
/// </summary>
/// <remarks>
/// <para>
/// Capability registration is deliberately <em>open</em>: the SDK does not enumerate the kinds of
/// thing a plugin may contribute. It provides the mechanism, and the contract types live in the
/// layer that owns them &#8212; <c>IDataSourceReader</c> and <c>IFeatureExporter</c> in Domain,
/// <c>IAIProvider</c> in the AI layer.
/// </para>
/// <para>
/// The consequence is that introducing an entirely new kind of capability &#8212; a CRS resolver,
/// a QA/QC rule pack, a UI panel &#8212; requires no change to this SDK at all. An enumeration of
/// capability kinds here would have to be edited for every new one, which is precisely the
/// modification the plugin system exists to avoid.
/// </para>
/// </remarks>
public interface IPluginRegistrationContext
{
    /// <summary>Gets the plugin's context.</summary>
    IPluginContext Context { get; }

    /// <summary>
    /// Gets the service collection backing the plugin's own service provider. Use for the
    /// plugin's internal dependencies; use <see cref="AddCapability{TContract}(Func{IServiceProvider, TContract})"/>
    /// for anything the host should see.
    /// </summary>
    IServiceCollection Services { get; }

    /// <summary>Contributes a capability instance to the host.</summary>
    /// <typeparam name="TContract">The contract type the host resolves by.</typeparam>
    /// <param name="instance">The capability implementation.</param>
    /// <returns>The same context, for chaining.</returns>
    IPluginRegistrationContext AddCapability<TContract>(TContract instance)
        where TContract : class;

    /// <summary>Contributes a capability created lazily from the plugin's service provider.</summary>
    /// <typeparam name="TContract">The contract type the host resolves by.</typeparam>
    /// <param name="factory">Factory invoked once, on first resolution.</param>
    /// <returns>The same context, for chaining.</returns>
    IPluginRegistrationContext AddCapability<TContract>(Func<IServiceProvider, TContract> factory)
        where TContract : class;

    /// <summary>Contributes a capability resolved from the plugin's service provider.</summary>
    /// <typeparam name="TContract">The contract type the host resolves by.</typeparam>
    /// <typeparam name="TImplementation">The concrete implementation, activated by the container.</typeparam>
    /// <returns>The same context, for chaining.</returns>
    IPluginRegistrationContext AddCapability<TContract, TImplementation>()
        where TContract : class
        where TImplementation : class, TContract;
}
