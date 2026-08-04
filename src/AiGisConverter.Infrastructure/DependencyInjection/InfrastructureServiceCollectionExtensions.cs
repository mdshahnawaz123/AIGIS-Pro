using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Infrastructure.FileSystem;
using AiGisConverter.Infrastructure.Security;
using AiGisConverter.Infrastructure.Threading;
using AiGisConverter.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.Infrastructure.DependencyInjection;

/// <summary>Composition-root entry point for the infrastructure layer.</summary>
public static class InfrastructureServiceCollectionExtensions
{
    /// <summary>Registers the cross-cutting services.</summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddInfrastructureLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddSingleton<IFileSystem, PhysicalFileSystem>();
        services.TryAddSingleton<ISecretResolver, EnvironmentSecretResolver>();
        services.TryAddSingleton<IBackgroundTaskQueue>(provider =>
            new BackgroundTaskQueue(
                provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<BackgroundTaskQueue>>()));

        services.AddHttpClient();

        return services;
    }
}
