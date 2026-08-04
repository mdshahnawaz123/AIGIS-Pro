using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Services.Conversion;
using AiGisConverter.Application.Services.Batch;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Factories;
using AiGisConverter.Gis.Profiles;
using AiGisConverter.QaQc.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Composition;

/// <summary>
/// Writes converted datasets, adapting the application's port to the GIS layer's exporters.
/// </summary>
/// <remarks>
/// The application layer references only Domain, so it cannot name an exporter. This adapter is
/// the one place that knows both sides, which is what lets a new output format be added to the GIS
/// layer without the pipeline learning anything.
/// </remarks>
public sealed class GisDatasetExportService : IDatasetExportService
{
    private readonly IExporterFactory _exporters;
    private readonly IProfileRepository _profiles;
    private readonly ILogger<GisDatasetExportService> _logger;

    /// <summary>Initializes a new instance of the <see cref="GisDatasetExportService"/> class.</summary>
    /// <param name="exporters">Resolves a writer by format.</param>
    /// <param name="profiles">Supplies the profile governing the export.</param>
    /// <param name="logger">Logger for export diagnostics.</param>
    public GisDatasetExportService(
        IExporterFactory exporters,
        IProfileRepository profiles,
        ILogger<GisDatasetExportService> logger)
    {
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(logger);

        _exporters = exporters;
        _profiles = profiles;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> ExportAsync(
        IReadOnlyList<GisDataset> datasets,
        DatasetExportRequest request,
        IProgress<Application.ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datasets);
        ArgumentNullException.ThrowIfNull(request);

        Result<ConversionProfile> profile = _profiles.Get(request.ProfileId ?? "generic-geojson");

        if (profile.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(profile.Error);
        }

        Result<IStreamingExporter> exporter = request.FormatKey is { Length: > 0 } key
            ? _exporters.Resolve(key)
            : _exporters.Resolve(profile.Value.ExportFormat ?? Domain.Enums.ExportFormat.GeoJson);

        if (exporter.IsFailure)
        {
            return Result.Failure<IReadOnlyList<string>>(exporter.Error);
        }

        List<string> written = [];

        foreach (GisDataset dataset in datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GisConversionContext context = new(
                profile.Value, dataset.CoordinateSystem, dataset.CoordinateSystem);

            ExportRequest exportRequest = new(
                Path.Combine(request.OutputDirectory, SafeFileName(dataset.FeatureClass.Name) + exporter.Value.FileExtension),
                dataset.FeatureClass,
                dataset.Schema,
                dataset.CoordinateSystem,
                context);

            Result<IReadOnlyList<string>> result = await exporter.Value
                .WriteAsync(exportRequest, ToAsyncEnumerable(dataset.Features, cancellationToken),
                    progress: null, cancellationToken)
                .ConfigureAwait(false);

            if (result.IsFailure)
            {
                // One layer failing does not abandon the rest: a partial delivery naming what is
                // missing is more useful than nothing at all.
                _logger.LogError("Dataset {Dataset} failed to export: {Reason}",
                    dataset.FeatureClass.Name, result.Error.Message);

                continue;
            }

            written.AddRange(result.Value);

            progress?.Report(new Application.ConversionProgress(
                "Export", $"Wrote {dataset.FeatureClass.Name}"));
        }

        return written.Count > 0 || datasets.Count == 0
            ? Result.Success<IReadOnlyList<string>>(written)
            : Result.Failure<IReadOnlyList<string>>(new Error(
                "Export.AllDatasetsFailed",
                "No dataset could be written. See the log for the per-layer reasons."));
    }

    /// <summary>
    /// Strips anything from a layer name that could escape the output directory.
    /// </summary>
    /// <remarks>
    /// The name reaching here has already passed through the profile's naming rules, which happen
    /// to collapse a traversal sequence to separators. That is a formatting rule, not a security
    /// control: a profile configured with an empty separator, or a future option to preserve exact
    /// names, would silently turn a layer called <c>..\..\startup</c> into a write outside the
    /// chosen folder. Defence in depth belongs at the point the path is built.
    /// </remarks>
    /// <param name="featureClassName">The layer name to sanitise.</param>
    /// <returns>A name safe to use as a single path segment.</returns>
    private static string SafeFileName(string featureClassName)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[featureClassName.Length];

        for (int i = 0; i < featureClassName.Length; i++)
        {
            char character = featureClassName[i];
            buffer[i] = Array.IndexOf(invalid, character) >= 0 || character is '.' or ' ' ? '_' : character;
        }

        string safe = new string(buffer).Trim('_');

        return safe.Length == 0 ? "layer" : safe;
    }

    /// <summary>
    /// Presents an already-materialised feature list as an async sequence.
    /// </summary>
    /// <remarks>
    /// The exporters stream, and this path does not: the domain's <c>IGeometryConverter</c> port
    /// returns a materialised list, and widening it would change a frozen contract. Large drawings
    /// should use the GIS engine's own streaming entry point rather than this pipeline.
    /// </remarks>
    private static async IAsyncEnumerable<GisFeature> ToAsyncEnumerable(
        IReadOnlyList<GisFeature> features,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        foreach (GisFeature feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();

            yield return feature;
        }

        await Task.CompletedTask.ConfigureAwait(false);
    }
}

/// <summary>Renders QA reports, adapting the application's port to the QA/QC layer's renderer.</summary>
public sealed class QaQcReportRendererAdapter : IQaReportRenderer
{
    private readonly ValidationReportRenderer _renderer;

    /// <summary>Initializes a new instance of the <see cref="QaQcReportRendererAdapter"/> class.</summary>
    /// <param name="renderer">The QA/QC renderer.</param>
    public QaQcReportRendererAdapter(ValidationReportRenderer renderer)
    {
        ArgumentNullException.ThrowIfNull(renderer);
        _renderer = renderer;
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> RenderAsync(
        ValidationReport report,
        string outputPathWithoutExtension,
        CancellationToken cancellationToken = default) =>
        _renderer.RenderAsync(report, outputPathWithoutExtension, cancellationToken);
}

/// <summary>
/// Creates an isolated conversion scope from the container.
/// </summary>
/// <remarks>
/// The change tracker is scoped, so two parallel conversions sharing one would be writing to the
/// same tracker. Only the composition root knows the container, which is why the factory lives
/// here rather than in the batch service that uses it.
/// </remarks>
public sealed class ServiceProviderConversionScopeFactory : IConversionScopeFactory
{
    private readonly IServiceScopeFactory _scopeFactory;

    /// <summary>Initializes a new instance of the <see cref="ServiceProviderConversionScopeFactory"/> class.</summary>
    /// <param name="scopeFactory">The container's scope factory.</param>
    public ServiceProviderConversionScopeFactory(IServiceScopeFactory scopeFactory)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        _scopeFactory = scopeFactory;
    }

    /// <inheritdoc />
    public IConversionScope Create() => new Scope(_scopeFactory.CreateAsyncScope());

    private sealed class Scope : IConversionScope
    {
        private readonly AsyncServiceScope _scope;

        public Scope(AsyncServiceScope scope)
        {
            _scope = scope;
            Service = scope.ServiceProvider.GetRequiredService<IConversionService>();
        }

        public IConversionService Service { get; }

        public ValueTask DisposeAsync() => _scope.DisposeAsync();
    }
}
