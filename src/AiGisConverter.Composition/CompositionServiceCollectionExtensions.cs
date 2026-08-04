using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Services.Batch;
using AiGisConverter.QaQc.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.Composition;

/// <summary>
/// Wires plugin-contributed capabilities into the layers that consume them.
/// </summary>
public static class CompositionServiceCollectionExtensions
{
    /// <summary>
    /// Registers the adapters that make plugin capabilities visible to the AI, CAD and GIS layers.
    /// Call after <c>AddAiLayer</c> and <c>AddPluginSystem</c>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddPluginIntegration(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IAIProviderSource, CapabilityAIProviderSource>());

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidationRuleSource, CapabilityValidationRuleSource>());

        services.TryAddSingleton<IDataSourceReaderCatalog, PluginAwareDataSourceReaderCatalog>();
        services.TryAddSingleton<IFeatureExporterCatalog, PluginAwareFeatureExporterCatalog>();
        services.TryAddSingleton<PluginBootstrapper>();

        // The seams that let the application layer coordinate GIS and QA/QC while referencing
        // neither. Each is the only place that knows both sides.
        services.TryAddSingleton<IDatasetExportService, GisDatasetExportService>();
        services.TryAddSingleton<IQaReportRenderer, QaQcReportRendererAdapter>();
        services.TryAddSingleton<IConversionScopeFactory, ServiceProviderConversionScopeFactory>();

        // Shared application state: the most recently converted drawing, read by the Mapping Editor.
        // A singleton so every screen sees the same current drawing; the pipeline's publish stage
        // fills it and Clear() empties it.
        services.TryAddSingleton<IConversionSession, AiGisConverter.Application.Services.ConversionSession>();

        return services;
    }
}
