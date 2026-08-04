using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.DependencyInjection;

/// <summary>
/// Default <see cref="IAIProviderBuilder"/>. Registers each provider as a singleton and exposes it
/// as <see cref="IAIProvider"/> wrapped in every registered <see cref="IAIProviderDecorator"/>.
/// </summary>
internal sealed class AIProviderBuilder : IAIProviderBuilder
{
    public AIProviderBuilder(IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        Services = services;
        Configuration = configuration;
    }

    public IServiceCollection Services { get; }

    public IConfiguration Configuration { get; }

    public IAIProviderBuilder AddProvider<TProvider>()
        where TProvider : class, IAIProvider
    {
        Services.TryAddSingleton<TProvider>();
        Services.AddSingleton<IAIProvider>(static sp => Decorate(sp, sp.GetRequiredService<TProvider>()));

        return this;
    }

    public IAIProviderBuilder AddProvider<TProvider, TOptions>(string providerKey, Action<TOptions>? configure = null)
        where TProvider : class, IAIProvider
        where TOptions : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);

        IConfigurationSection section = Configuration.GetSection($"{AiOptions.ProvidersSectionName}:{providerKey}");

        OptionsBuilder<TOptions> optionsBuilder = Services
            .AddOptions<TOptions>(providerKey)
            .Bind(section)
            .ValidateDataAnnotations();

        // Options are also registered unnamed so a provider can inject IOptionsMonitor<TOptions>
        // without knowing the named-options key.
        Services.AddOptions<TOptions>().Bind(section).ValidateDataAnnotations();

        if (configure is not null)
        {
            optionsBuilder.Configure(configure);
            Services.Configure(configure);
        }

        return AddProvider<TProvider>();
    }

    /// <summary>Applies every registered decorator, lowest order outermost.</summary>
    /// <param name="serviceProvider">The container.</param>
    /// <param name="provider">The raw provider.</param>
    /// <returns>The decorated provider.</returns>
    private static IAIProvider Decorate(IServiceProvider serviceProvider, IAIProvider provider)
    {
        IAIProvider decorated = provider;

        foreach (IAIProviderDecorator decorator in serviceProvider
            .GetServices<IAIProviderDecorator>()
            .OrderByDescending(static d => d.Order))
        {
            decorated = decorator.Decorate(decorated);
        }

        return decorated;
    }
}
