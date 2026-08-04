using AiGisConverter.Ai.DependencyInjection;

namespace AiGisConverter.Ai.Providers.RuleBased;

/// <summary>
/// Registration extension for <see cref="RuleBasedProvider"/>.
/// </summary>
public static class RuleBasedProviderBuilderExtensions
{
    /// <summary>Registers the offline rule-based provider.</summary>
    /// <param name="builder">The provider builder.</param>
    /// <param name="configure">Optional code-based override of the bound options.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static IAIProviderBuilder AddRuleBasedProvider(
        this IAIProviderBuilder builder,
        Action<RuleBasedOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddProvider<RuleBasedProvider, RuleBasedOptions>(RuleBasedProvider.ProviderKey, configure);
    }
}
