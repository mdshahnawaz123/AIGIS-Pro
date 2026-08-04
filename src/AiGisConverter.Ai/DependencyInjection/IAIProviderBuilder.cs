using AiGisConverter.Ai.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AiGisConverter.Ai.DependencyInjection;

/// <summary>
/// Registration surface for AI providers.
/// </summary>
/// <remarks>
/// A provider ships one extension method on this interface. That method is the provider's only
/// contact with the composition root, and it is additive: no core file is edited when a provider
/// is introduced or removed.
/// </remarks>
public interface IAIProviderBuilder
{
    /// <summary>Gets the service collection being configured.</summary>
    IServiceCollection Services { get; }

    /// <summary>Gets the application configuration, rooted at the application level.</summary>
    IConfiguration Configuration { get; }

    /// <summary>Registers a provider that needs no options of its own.</summary>
    /// <typeparam name="TProvider">Concrete provider type.</typeparam>
    /// <returns>The same builder, for chaining.</returns>
    IAIProviderBuilder AddProvider<TProvider>()
        where TProvider : class, IAIProvider;

    /// <summary>
    /// Registers a provider together with its own options, bound from
    /// <c>Ai:Providers:&lt;providerKey&gt;</c>.
    /// </summary>
    /// <typeparam name="TProvider">Concrete provider type.</typeparam>
    /// <typeparam name="TOptions">Provider-specific options type.</typeparam>
    /// <param name="providerKey">The provider key, matching its configuration sub-section.</param>
    /// <param name="configure">Optional code-based override applied after configuration binding.</param>
    /// <returns>The same builder, for chaining.</returns>
    IAIProviderBuilder AddProvider<TProvider, TOptions>(string providerKey, Action<TOptions>? configure = null)
        where TProvider : class, IAIProvider
        where TOptions : class;
}
