using AiGisConverter.Ai.Abstractions;

namespace AiGisConverter.Ai.Factories;

/// <summary>
/// The built-in source: providers registered directly with the container by
/// <c>AddAiLayer(..., providers =&gt; ...)</c>.
/// </summary>
public sealed class ServiceProviderAIProviderSource : IAIProviderSource
{
    private readonly IEnumerable<IAIProvider> _providers;

    /// <summary>Initializes a new instance of the <see cref="ServiceProviderAIProviderSource"/> class.</summary>
    /// <param name="providers">Providers registered with the container.</param>
    public ServiceProviderAIProviderSource(IEnumerable<IAIProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        _providers = providers;
    }

    /// <inheritdoc />
    public IEnumerable<IAIProvider> GetProviders() => _providers;
}
