using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Cad.Factories;
using AiGisConverter.Cad.Options;
using AiGisConverter.Cad.Providers.AutoCad;
using AiGisConverter.Cad.Providers.Dxf;
using AiGisConverter.Domain.Abstractions.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.Cad.DependencyInjection;

/// <summary>
/// Composition-root entry point for the CAD layer.
/// </summary>
public static class CadServiceCollectionExtensions
{
    /// <summary>
    /// Registers the CAD providers and exposes each as a domain <see cref="IDataSourceReader"/>.
    /// </summary>
    /// <remarks>
    /// The DWG backend is registered with <c>TryAdd</c>, so a licensed build can register a real
    /// <see cref="IDwgBackend"/> beforehand and this call will leave it alone.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>Cad</c> section.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddCadLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<CadOptions>()
            .Bind(configuration.GetSection(CadOptions.SectionName))
            .ValidateDataAnnotations();

        services.TryAddSingleton<IDwgBackend, UnavailableDwgBackend>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICadProvider, DxfProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<ICadProvider, AutoCadProvider>());

        services.TryAddSingleton<ICadProviderFactory, CadProviderFactory>();

        // Each provider becomes its own reader, so the catalogue lists formats rather than a
        // single opaque "CAD" entry.
        services.AddSingleton<IDataSourceReader>(static sp =>
            new CadDataSourceReader(sp.GetRequiredService<ICadProviderFactory>().ResolveByKey(DxfProvider.ProviderKey)!));

        services.AddSingleton<IDataSourceReader>(static sp =>
            new CadDataSourceReader(sp.GetRequiredService<ICadProviderFactory>().ResolveByKey(AutoCadProvider.ProviderKey)!));

        return services;
    }
}
