using AiGisConverter.Ai.Models;

namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// Resolves providers by key. The only place in the layer that reads
/// <c>Ai:ActiveProvider</c>, and it does so without knowing any provider key in advance.
/// </summary>
public interface IAIProviderFactory
{
    /// <summary>Gets the provider named by <c>Ai:ActiveProvider</c>.</summary>
    /// <returns>The active provider.</returns>
    /// <exception cref="Exceptions.AIProviderNotRegisteredException">
    /// The configured key matches no registered provider.
    /// </exception>
    IAIProvider GetActiveProvider();

    /// <summary>Gets the provider named by <c>Ai:FallbackProvider</c>, if one is configured.</summary>
    /// <returns>The fallback provider, or <see langword="null"/> when none is configured or registered.</returns>
    IAIProvider? GetFallbackProvider();

    /// <summary>Gets a provider by key.</summary>
    /// <param name="key">The provider key, matched case-insensitively.</param>
    /// <returns>The requested provider.</returns>
    /// <exception cref="Exceptions.AIProviderNotRegisteredException">No provider has that key.</exception>
    IAIProvider GetProvider(string key);

    /// <summary>Attempts to get a provider by key.</summary>
    /// <param name="key">The provider key, matched case-insensitively.</param>
    /// <param name="provider">The resolved provider, when found.</param>
    /// <returns><see langword="true"/> when a provider with that key is registered.</returns>
    bool TryGetProvider(string key, out IAIProvider? provider);

    /// <summary>Describes every registered provider, for UI discovery and diagnostics.</summary>
    /// <returns>Metadata for all registered providers, ordered by key.</returns>
    IReadOnlyList<AIProviderMetadata> GetRegisteredProviders();

    /// <summary>
    /// Discards the cached provider index so the next call re-queries every
    /// <see cref="IAIProviderSource"/>. Call after plugins are loaded or unloaded.
    /// </summary>
    void Refresh();
}
