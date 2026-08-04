using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.QaQc.Abstractions;
using AiGisConverter.QaQc.Engine;
using AiGisConverter.QaQc.Options;
using AiGisConverter.QaQc.Reporting;
using AiGisConverter.QaQc.Rules.Attribute;
using AiGisConverter.QaQc.Rules.Crs;
using AiGisConverter.QaQc.Rules.Geometry;
using AiGisConverter.QaQc.Rules.Topology;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.QaQc.DependencyInjection;

/// <summary>Composition-root entry point for the QA/QC layer.</summary>
public static class QaQcServiceCollectionExtensions
{
    /// <summary>
    /// Registers the rule engine, the built-in rules and the report writers.
    /// </summary>
    /// <remarks>
    /// Rules are registered as an enumerable so the built-in source picks them all up. A plugin
    /// contributing an <see cref="IValidationRule"/> is surfaced by a second source added in the
    /// composition layer; neither source knows about the other.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>QaQc</c> section.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddQaQcLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<QaQcOptions>()
            .Bind(configuration.GetSection(QaQcOptions.SectionName))
            .ValidateDataAnnotations();

        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IValidationRule, OverlappingFeaturesRule>(),
            ServiceDescriptor.Singleton<IValidationRule, DanglingEndpointRule>(),
            ServiceDescriptor.Singleton<IValidationRule, SliverPolygonRule>(),
            ServiceDescriptor.Singleton<IValidationRule, RequiredFieldRule>(),
            ServiceDescriptor.Singleton<IValidationRule, UniqueFieldRule>(),
            ServiceDescriptor.Singleton<IValidationRule, FormatLimitRule>(),
            ServiceDescriptor.Singleton<IValidationRule, CoordinateRangeRule>(),
            ServiceDescriptor.Singleton<IValidationRule, DatasetIntegrityRule>(),
            ServiceDescriptor.Singleton<IValidationRule, AiGisConverter.QaQc.Rules.Semantic.MissingHostRule>(),
        ]);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IValidationRuleSource, BuiltInValidationRuleSource>());

        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IValidationReportWriter, HtmlValidationReportWriter>(),
            ServiceDescriptor.Singleton<IValidationReportWriter, CsvValidationReportWriter>(),
            ServiceDescriptor.Singleton<IValidationReportWriter, JsonValidationReportWriter>(),
        ]);

        services.TryAddSingleton<IQaQcEngine, QaQcEngine>();
        services.TryAddSingleton<ValidationReportRenderer>();

        return services;
    }
}
