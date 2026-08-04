using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Crs;
using AiGisConverter.Gis.Exporters.Csv;
using AiGisConverter.Gis.Exporters.GeoJson;
using AiGisConverter.Gis.Exporters.GeoPackage;
using AiGisConverter.Gis.Exporters.Kml;
using AiGisConverter.Gis.Exporters.Shapefile;
using AiGisConverter.Gis.Exporters.Wkb;
using AiGisConverter.Gis.Exporters.Wkt;
using AiGisConverter.Gis.Factories;
using AiGisConverter.Gis.Features;
using AiGisConverter.Gis.Gdal;
using AiGisConverter.Gis.Geometry;
using AiGisConverter.Gis.Indexing;
using AiGisConverter.Gis.Options;
using AiGisConverter.Gis.Pipeline;
using AiGisConverter.Gis.Profiles;
using AiGisConverter.Gis.Spatial.Abstractions;
using AiGisConverter.Gis.Spatial.Analysis;
using AiGisConverter.Gis.Spatial.Operations;
using AiGisConverter.Gis.Spatial.Query;
using AiGisConverter.Gis.Spatial.Repair;
using AiGisConverter.Gis.Spatial.Topology;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AiGisConverter.Gis.DependencyInjection;

/// <summary>
/// Composition-root entry point for the GIS layer.
/// </summary>
public static class GisServiceCollectionExtensions
{
    /// <summary>
    /// Registers the GIS engine: geometry stages, attribute mapping, profiles, every exporter, the
    /// CRS registry and transformer, and the conversion engine.
    /// </summary>
    /// <remarks>
    /// The spatial index is deliberately transient. It is bulk-loaded and immutable once built, so
    /// a shared instance would be wrong; callers that need one build it for the query they are
    /// about to run.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration containing the <c>Gis</c> section.</param>
    /// <returns>The same service collection, for chaining.</returns>
    public static IServiceCollection AddGisLayer(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services
            .AddOptions<GisOptions>()
            .Bind(configuration.GetSection(GisOptions.SectionName))
            .ValidateDataAnnotations();

        services.TryAddSingleton<GdalEnvironment>();

        services.TryAddSingleton<IGeometryMapper, GeometryMapper>();
        services.TryAddSingleton<IGeometryValidator, GeometryValidator>();
        services.TryAddSingleton<IGeometryRepairer, GeometryRepairer>();
        services.TryAddSingleton<IGeometrySimplifier, GeometrySimplifier>();
        services.TryAddSingleton<IAttributeMapper, AttributeMapper>();

        services.TryAddSingleton<ICrsRegistry, GdalCrsRegistry>();
        services.TryAddSingleton<ICoordinateTransformer, GdalCoordinateTransformer>();
        services.TryAddSingleton<ICrsDetector, FallbackCrsDetector>();

        // CRS catalogue: full EPSG/PROJ search and area-of-use lookup, backed by proj.db directly.
        services.TryAddSingleton<ProjDbLocator>();
        services.TryAddSingleton<ICrsCatalog, ProjDbCrsCatalog>();
        services.TryAddSingleton<ICrsSuggester, CrsSuggester>();
        services.TryAddSingleton<ICrsPreferences, JsonCrsPreferences>();
        services.TryAddSingleton<ICrsValidator, CrsValidator>();

        services.TryAddSingleton<IProfileRepository, ProfileRepository>();
        services.TryAddSingleton<IFeatureBuilder, FeatureBuilder>();

        services.TryAddTransient<ISpatialIndex, RTreeSpatialIndex>();

        AddSpatialProcessing(services);

        AddExporters(services);

        services.TryAddSingleton<IExporterFactory, ExporterFactory>();
        services.TryAddSingleton<GisConversionEngine>();
        services.TryAddSingleton<IGeometryConverter>(static sp => sp.GetRequiredService<GisConversionEngine>());

        return services;
    }

    /// <summary>
    /// Registers the spatial processing engine.
    /// </summary>
    /// <remarks>
    /// The topology engine, operations and analysis are stateless and shared. The query engine is
    /// transient because it owns an index, and an index is bulk-loaded and immutable once built:
    /// a shared instance would be wrong the moment a second caller wanted a different feature set.
    /// </remarks>
    private static void AddSpatialProcessing(IServiceCollection services)
    {
        services.TryAddSingleton<ITopologyEngine, TopologyEngine>();
        services.TryAddSingleton<ISpatialOperations, SpatialOperations>();
        services.TryAddSingleton<ISpatialAnalysis, SpatialAnalysis>();
        services.TryAddSingleton<IGeometrySnapper, GeometrySnapper>();

        services.TryAddTransient<ISpatialQueryEngine, SpatialQueryEngine>();
    }

    /// <summary>Registers every writer. Order decides nothing; resolution is by format key.</summary>
    private static void AddExporters(IServiceCollection services)
    {
        services.TryAddEnumerable(
        [
            ServiceDescriptor.Singleton<IStreamingExporter, StreamingGeoJsonExporter>(),
            ServiceDescriptor.Singleton<IStreamingExporter, StreamingCsvExporter>(),
            ServiceDescriptor.Singleton<IStreamingExporter, StreamingKmlExporter>(),
            ServiceDescriptor.Singleton<IStreamingExporter, StreamingWktExporter>(),
            ServiceDescriptor.Singleton<IStreamingExporter, StreamingWkbExporter>(),
            ServiceDescriptor.Singleton<IStreamingExporter, ShapefileExporter>(),
            ServiceDescriptor.Singleton<IStreamingExporter, GeoPackageExporter>(),
        ]);
    }
}
