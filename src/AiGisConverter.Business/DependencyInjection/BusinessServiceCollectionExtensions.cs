using AiGisConverter.Business.Classification;
using AiGisConverter.Domain.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Business.DependencyInjection;

/// <summary>
/// Composition-root entry point for the business layer.
/// </summary>
public static class BusinessServiceCollectionExtensions
{
    /// <summary>
    /// Registers the business layer services.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddAiGisBusiness(this IServiceCollection services)
    {
        services.AddSingleton<ClassificationEngine>();
        services.AddSingleton<IRuleEngine>(sp => sp.GetRequiredService<ClassificationEngine>());
        
        services.AddSingleton<RuleProfileLoader>(sp => 
        {
            var engine = sp.GetRequiredService<ClassificationEngine>();
            var logger = sp.GetRequiredService<ILogger<RuleProfileLoader>>();
            var loader = new RuleProfileLoader(engine, logger);
            loader.LoadProfiles(); // Load on startup
            return loader;
        });

        // Ensure loader is instantiated at startup
        services.AddHostedService<RuleProfileLoaderHostedService>();

        return services;
    }
}

internal sealed class RuleProfileLoaderHostedService : Microsoft.Extensions.Hosting.IHostedService
{
    private readonly RuleProfileLoader _loader;

    public RuleProfileLoaderHostedService(RuleProfileLoader loader)
    {
        _loader = loader;
    }

    public System.Threading.Tasks.Task StartAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
    public System.Threading.Tasks.Task StopAsync(System.Threading.CancellationToken cancellationToken) => System.Threading.Tasks.Task.CompletedTask;
}
