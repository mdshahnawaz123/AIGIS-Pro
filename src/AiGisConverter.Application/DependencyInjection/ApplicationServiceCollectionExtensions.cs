using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Jobs;
using AiGisConverter.Application.Notifications;
using AiGisConverter.Application.Pipelines;
using AiGisConverter.Application.Pipelines.Steps;
using AiGisConverter.Application.Services.Batch;
using AiGisConverter.Application.Services.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.Application.DependencyInjection;

/// <summary>Composition-root entry point for the application layer.</summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the pipeline, its stages, and the services that coordinate them.
    /// </summary>
    /// <remarks>
    /// Stages are registered as an enumerable and ordered by the pipeline, so adding one is a
    /// registration rather than an edit to the code that runs them.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
        [
            ServiceDescriptor.Scoped<IPipelineStage, ReadSourceStage>(),
            ServiceDescriptor.Scoped<IPipelineStage, DetectCoordinateSystemStage>(),
            ServiceDescriptor.Scoped<IPipelineStage, ClassifyStage>(),
            ServiceDescriptor.Scoped<IPipelineStage, ConvertGeometryStage>(),
            ServiceDescriptor.Scoped<IPipelineStage, ValidateStage>(),
            ServiceDescriptor.Scoped<IPipelineStage, ExportStage>(),
            ServiceDescriptor.Scoped<IPipelineStage, PublishSessionStage>(),
        ]);

        services.TryAddScoped<IConversionPipeline, ConversionPipeline>();
        services.TryAddScoped<IConversionService, ConversionService>();

        services.TryAddSingleton<INotificationService, NotificationService>();
        services.TryAddSingleton<IBatchConversionService, BatchConversionService>();
        services.TryAddSingleton<IJobEngine>(provider => new JobEngine(
            provider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<JobEngine>>()));

        return services;
    }
}
