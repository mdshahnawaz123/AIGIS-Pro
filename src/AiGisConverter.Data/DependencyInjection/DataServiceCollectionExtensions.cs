using AiGisConverter.Data.Abstractions;
using AiGisConverter.Data.Context;
using AiGisConverter.Data.Options;
using AiGisConverter.Data.Repositories;
using AiGisConverter.Domain.Abstractions.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Data.DependencyInjection;

/// <summary>Composition-root entry point for the data layer.</summary>
public static class DataServiceCollectionExtensions
{
    /// <summary>
    /// Registers the database context, repositories and unit of work.
    /// </summary>
    /// <remarks>
    /// The context is scoped, as Entity Framework requires: it tracks changes, and a singleton
    /// context would accumulate every entity the application ever touched and hand stale instances
    /// to unrelated callers.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>Database</c> section.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddDataLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<DataOptions>()
            .Bind(configuration.GetSection(DataOptions.SectionName))
            .ValidateDataAnnotations();

        services.AddDbContext<AiGisConverterDbContext>((provider, builder) =>
        {
            DataOptions options = provider.GetRequiredService<IOptions<DataOptions>>().Value;

            builder.UseSqlite(
                Environment.ExpandEnvironmentVariables(options.ConnectionString),
                sqlite => sqlite.CommandTimeout(options.CommandTimeoutSeconds));

            if (options.EnableSensitiveDataLogging)
            {
                builder.EnableSensitiveDataLogging();
            }
        });

        services.TryAddSingleton<IDomainEventDispatcher, NullDomainEventDispatcher>();

        services.TryAddScoped<IConversionProjectRepository, ConversionProjectRepository>();
        services.TryAddScoped<IConversionRunRepository, ConversionRunRepository>();
        services.TryAddScoped<IValidationReportRepository, ValidationReportRepository>();
        services.TryAddScoped<IUnitOfWork, UnitOfWork.UnitOfWork>();

        services.TryAddScoped<DatabaseInitialiser>();

        return services;
    }
}
