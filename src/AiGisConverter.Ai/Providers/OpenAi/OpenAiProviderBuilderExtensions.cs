using AiGisConverter.Ai.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace AiGisConverter.Ai.Providers.OpenAi;

/// <summary>
/// Registration extension for <see cref="OpenAiProvider"/>.
/// </summary>
public static class OpenAiProviderBuilderExtensions
{
    /// <summary>Registers the OpenAI provider and its HTTP client.</summary>
    /// <param name="builder">The provider builder.</param>
    /// <param name="configure">Optional code-based override of the bound options.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IAIProviderBuilder AddOpenAiProvider(
        this IAIProviderBuilder builder,
        Action<OpenAiOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHttpClient(OpenAiProvider.HttpClientName);

        return builder.AddProvider<OpenAiProvider, OpenAiOptions>(OpenAiProvider.ProviderKey, configure);
    }
}
