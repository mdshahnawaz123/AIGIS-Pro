using AiGisConverter.Ai.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.Ai.Providers.Onnx;

/// <summary>
/// Registration extension for <see cref="OnnxProvider"/>.
/// </summary>
public static class OnnxProviderBuilderExtensions
{
    /// <summary>Registers the local ONNX Runtime provider, its session and its feature encoder.</summary>
    /// <param name="builder">The provider builder.</param>
    /// <param name="configure">Optional code-based override of the bound options.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IAIProviderBuilder AddOnnxProvider(
        this IAIProviderBuilder builder,
        Action<OnnxOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.TryAddSingleton<IOnnxSessionProvider, OnnxSessionProvider>();
        builder.Services.TryAddSingleton<HashingFeatureEncoder>();

        return builder.AddProvider<OnnxProvider, OnnxOptions>(OnnxProvider.ProviderKey, configure);
    }
}
