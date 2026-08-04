using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Plugins.Hosting;

namespace AiGisConverter.Composition;

/// <summary>
/// Exposes plugin-contributed <see cref="IAIProvider"/> capabilities to the AI layer.
/// </summary>
/// <remarks>
/// This adapter is the entire reason <c>AiGisConverter.Ai</c> does not reference the plugin host.
/// A third-party AI provider arriving as a plugin becomes selectable through
/// <c>Ai:ActiveProvider</c> exactly like a built-in one.
/// </remarks>
public sealed class CapabilityAIProviderSource : IAIProviderSource
{
    private readonly ICapabilityRegistry _registry;

    /// <summary>Initializes a new instance of the <see cref="CapabilityAIProviderSource"/> class.</summary>
    /// <param name="registry">The host capability registry.</param>
    public CapabilityAIProviderSource(ICapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        _registry = registry;
    }

    /// <inheritdoc />
    public IEnumerable<IAIProvider> GetProviders() => _registry.GetCapabilities<IAIProvider>();
}
