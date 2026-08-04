namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// Supplies providers to <see cref="IAIProviderFactory"/>.
/// </summary>
/// <remarks>
/// <para>
/// Providers do not all exist when the container is built. Built-in providers are registered at
/// composition time; plugin-contributed providers appear only after the plugin host has finished
/// discovery, which happens later, during application start-up.
/// </para>
/// <para>
/// Indirecting through a source lets the factory ask "what providers are there now?" instead of
/// being handed a list that was correct at construction and stale a second afterwards.
/// </para>
/// </remarks>
public interface IAIProviderSource
{
    /// <summary>Gets the providers this source currently offers.</summary>
    /// <returns>The available providers. May differ between calls.</returns>
    IEnumerable<IAIProvider> GetProviders();
}
