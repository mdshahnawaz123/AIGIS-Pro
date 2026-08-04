using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Caching;
using AiGisConverter.Ai.Decorators;
using AiGisConverter.Ai.Factories;
using AiGisConverter.Ai.Features;
using AiGisConverter.Ai.Options;
using AiGisConverter.Ai.Prompting;
using AiGisConverter.Ai.Services;
using AiGisConverter.Domain.Abstractions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.Ai.DependencyInjection;

/// <summary>
/// Composition-root entry point for the AI layer.
/// </summary>
public static class AiServiceCollectionExtensions
{
    /// <summary>
    /// Registers the AI layer: options, prompt pipeline, cache, decorators, provider factory and
    /// the <see cref="IAiClassifier"/> domain port. Providers themselves are opt-in.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>Ai</c> section.</param>
    /// <param name="configureProviders">Callback in which providers are registered.</param>
    /// <returns>The same service collection, for chaining.</returns>
    /// <example>
    /// <code>
    /// services.AddAiLayer(configuration, providers => providers
    ///     .AddRuleBasedProvider()
    ///     .AddOllamaProvider()
    ///     .AddOpenAiProvider()
    ///     .AddOnnxProvider());
    /// </code>
    /// </example>
    public static IServiceCollection AddAiLayer(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IAIProviderBuilder> configureProviders)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(configureProviders);

        services
            .AddOptions<AiOptions>()
            .Bind(configuration.GetSection(AiOptions.SectionName))
            .ValidateDataAnnotations();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ISubjectDescriptor, SubjectDescriptor>();
        services.TryAddSingleton<IChatPromptBuilder, ClassificationPromptBuilder>();
        services.TryAddSingleton<IClassificationResponseParser, JsonClassificationResponseParser>();
        services.TryAddSingleton<IAIResponseCache, InMemoryAIResponseCache>();
        services.TryAddSingleton<AIRequestCacheKeyFactory>();

        // Cross-cutting concerns, applied to every provider. Additional concerns are added by
        // registering further IAIProviderDecorator implementations; no provider is aware of them.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIProviderDecorator, LoggingAIProviderDecorator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIProviderDecorator, ResilienceAIProviderDecorator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IAIProviderDecorator, CachingAIProviderDecorator>());

        // The built-in source exposes providers registered directly with the container.
        // The composition root adds a second source for plugin-contributed providers.
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAIProviderSource, ServiceProviderAIProviderSource>());

        services.TryAddSingleton<IAIProviderFactory, AIProviderFactory>();
        services.TryAddSingleton<IAiClassifier, AiClassificationService>();

        configureProviders(new AIProviderBuilder(services, configuration));

        return services;
    }
}
