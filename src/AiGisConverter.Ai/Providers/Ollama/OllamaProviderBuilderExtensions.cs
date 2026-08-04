using AiGisConverter.Ai.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace AiGisConverter.Ai.Providers.Ollama;

/// <summary>
/// Registration extension for <see cref="OllamaProvider"/>.
/// </summary>
public static class OllamaProviderBuilderExtensions
{
    /// <summary>Registers the Ollama local-LLM provider and its HTTP client.</summary>
    /// <param name="builder">The provider builder.</param>
    /// <param name="configure">Optional code-based override of the bound options.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IAIProviderBuilder AddOllamaProvider(
        this IAIProviderBuilder builder,
        Action<OllamaOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient(OllamaProvider.HttpClientName);

        return builder.AddProvider<OllamaProvider, OllamaOptions>(OllamaProvider.ProviderKey, configure);
    }
}
