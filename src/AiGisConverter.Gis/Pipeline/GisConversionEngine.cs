using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Factories;
using AiGisConverter.Gis.Options;
using AiGisConverter.Gis.Profiles;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Pipeline;

/// <summary>
/// Orchestrates the GIS side of a conversion.
/// </summary>
/// <remarks>
/// <para>
/// Two entry points, deliberately. <see cref="ConvertAsync"/> satisfies the frozen domain port
/// <see cref="IGeometryConverter"/>, whose signature returns a materialised list &#8212; fine for
/// the interactive path, where the user is looking at one drawing and wants a map preview.
/// </para>
/// <para>
/// <see cref="ConvertAndExportAsync"/> is the batch path and never materialises anything: features
/// stream from the builder straight into the writer. The domain port could not express that
/// without changing its signature, and the architecture is frozen, so the streaming capability is
/// offered alongside rather than by widening the port. Callers converting large drawings must use
/// it; the summary and the QA report say so.
/// </para>
/// </remarks>
public sealed class GisConversionEngine : IGeometryConverter
{
    private readonly IFeatureBuilder _featureBuilder;
    private readonly IAttributeMapper _attributeMapper;
    private readonly IGeometryMapper _geometryMapper;
    private readonly ICrsRegistry _crsRegistry;
    private readonly IProfileRepository _profiles;
    private readonly IExporterFactory _exporters;
    private readonly IOptionsMonitor<GisOptions> _options;
    private readonly ILogger<GisConversionEngine> _logger;

    /// <summary>Initializes a new instance of the <see cref="GisConversionEngine"/> class.</summary>
    /// <param name="featureBuilder">Produces features from source elements.</param>
    /// <param name="attributeMapper">Derives schemas.</param>
    /// <param name="geometryMapper">Classifies geometry families.</param>
    /// <param name="crsRegistry">Resolves coordinate systems.</param>
    /// <param name="profiles">Supplies conversion profiles.</param>
    /// <param name="exporters">Resolves writers.</param>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the engine.</param>
    public GisConversionEngine(
        IFeatureBuilder featureBuilder,
        IAttributeMapper attributeMapper,
        IGeometryMapper geometryMapper,
        ICrsRegistry crsRegistry,
        IProfileRepository profiles,
        IExporterFactory exporters,
        IOptionsMonitor<GisOptions> options,
        ILogger<GisConversionEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(featureBuilder);
        ArgumentNullException.ThrowIfNull(attributeMapper);
        ArgumentNullException.ThrowIfNull(geometryMapper);
        ArgumentNullException.ThrowIfNull(crsRegistry);
        ArgumentNullException.ThrowIfNull(profiles);
        ArgumentNullException.ThrowIfNull(exporters);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _featureBuilder = featureBuilder;
        _attributeMapper = attributeMapper;
        _geometryMapper = geometryMapper;
        _crsRegistry = crsRegistry;
        _profiles = profiles;
        _exporters = exporters;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<GisDataset>>> ConvertAsync(
        SourceDocument document,
        IReadOnlyDictionary<string, AiGisConverter.Domain.Entities.Ai.ClassificationResult> classification,
        CoordinateSystem sourceSystem,
        CoordinateSystem targetSystem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(classification);

        Result<ConversionProfile> profile = _profiles.Get(_options.CurrentValue.DefaultProfile);

        if (profile.IsFailure)
        {
            return Result.Failure<IReadOnlyList<GisDataset>>(profile.Error);
        }

        GisConversionContext context = new(profile.Value, sourceSystem, targetSystem);
        List<GisDataset> datasets = [];

        var virtualLayers = GroupElements(document, classification, context);

        foreach (SourceLayer layer in virtualLayers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (layer.Elements.Count == 0)
            {
                continue;
            }

            FeatureClass featureClass = ResolveFeatureClass(layer, classification, context);
            GisAttributeSchema schema = _attributeMapper.BuildSchema(layer, context.Profile);

            List<GisFeature> features = [];

            await foreach (GisFeature feature in _featureBuilder
                .BuildAsync(layer, featureClass, schema, context, cancellationToken)
                .ConfigureAwait(false))
            {
                features.Add(feature);
            }

            if (features.Count > 0)
            {
                datasets.Add(new GisDataset(featureClass, targetSystem, schema, features));
            }
        }

        _logger.LogInformation(
            "Converted {LayerCount} source layers into {DatasetCount} datasets ({Written} features, {Skipped} skipped, {Repaired} repaired).",
            document.Layers.Count,
            datasets.Count,
            context.FeaturesWritten,
            context.FeaturesSkipped,
            context.GeometriesRepaired);

        return Result.Success<IReadOnlyList<GisDataset>>(datasets);
    }

    /// <summary>
    /// Converts and writes in one streaming pass. Nothing larger than a single feature is held.
    /// </summary>
    /// <param name="document">The source document.</param>
    /// <param name="classification">The feature class assigned to each source layer.</param>
    /// <param name="request">Where and how to write.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>The outcome, including the QA report.</returns>
    public async Task<Result<GisConversionOutcome>> ConvertAndExportAsync(
        SourceDocument document,
        IReadOnlyDictionary<string, AiGisConverter.Domain.Entities.Ai.ClassificationResult> classification,
        StreamingExportRequest request,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(request);

        Result<ConversionProfile> profile = _profiles.Get(request.ProfileId ?? _options.CurrentValue.DefaultProfile);

        if (profile.IsFailure)
        {
            return Result.Failure<GisConversionOutcome>(profile.Error);
        }

        Result<CoordinateSystem> target = ResolveTarget(profile.Value, request);

        if (target.IsFailure)
        {
            return Result.Failure<GisConversionOutcome>(target.Error);
        }

        Result<IStreamingExporter> exporter = ResolveExporter(profile.Value, request);

        if (exporter.IsFailure)
        {
            return Result.Failure<GisConversionOutcome>(exporter.Error);
        }

        GisConversionContext context = new(profile.Value, request.SourceCoordinateSystem, target.Value);
        List<string> written = [];

        var virtualLayers = GroupElements(document, classification, context);

        foreach (SourceLayer layer in virtualLayers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (layer.Elements.Count == 0)
            {
                continue;
            }

            FeatureClass featureClass = ResolveFeatureClass(layer, classification, context);
            GisAttributeSchema schema = _attributeMapper.BuildSchema(layer, context.Profile);

            string outputPath = Path.Combine(
                request.OutputDirectory,
                featureClass.Name + exporter.Value.FileExtension);

            ExportRequest exportRequest = new(outputPath, featureClass, schema, target.Value, context);

            Result<IReadOnlyList<string>> result = await exporter.Value.WriteAsync(
                exportRequest,
                _featureBuilder.BuildAsync(layer, featureClass, schema, context, cancellationToken),
                progress,
                cancellationToken).ConfigureAwait(false);

            if (result.IsFailure)
            {
                context.Record(ValidationIssue.Create(
                    IssueSeverity.Critical,
                    IssueCategory.Export,
                    result.Error.Code,
                    result.Error.Message).ForLayer(LayerName.Create(layer.Name)));

                // One layer failing does not abandon the rest: a partial delivery with a report
                // naming what is missing is more useful than nothing at all.
                _logger.LogError("Layer {Layer} failed to export: {Reason}", layer.Name, result.Error.Message);
                continue;
            }

            written.AddRange(result.Value);
        }

        ValidationReport report = new(request.RunId, context.Issues);

        return Result.Success(new GisConversionOutcome(
            written,
            report,
            context.FeaturesWritten,
            context.FeaturesSkipped,
            context.GeometriesRepaired,
            target.Value));
    }

    private FeatureClass ResolveFeatureClass(
        SourceLayer layer,
        IReadOnlyDictionary<string, AiGisConverter.Domain.Entities.Ai.ClassificationResult> classification,
        GisConversionContext context)
    {
        // Since we grouped elements by classification, we can just look at the first element
        if (layer.Elements.Count > 0 && classification.TryGetValue(layer.Elements[0].Id, out var assigned))
        {
            GeometryKind dominant = DominantGeometry(layer.Elements);
            return FeatureClass.Create(context.Profile.ResolveLayerName(assigned.Label), dominant);
        }

        // No classification means the layer keeps its own name, and the geometry family is taken
        // from the first element that has geometry rather than assumed.
        GeometryKind kind = layer.Elements
            .Select(element => _geometryMapper.Classify(element.Geometry))
            .FirstOrDefault(static k => k != GeometryKind.Unknown);

        return FeatureClass.Create(context.Profile.ResolveLayerName(layer.Name), kind);
    }

    private IReadOnlyList<SourceLayer> GroupElements(
        SourceDocument document,
        IReadOnlyDictionary<string, AiGisConverter.Domain.Entities.Ai.ClassificationResult> classification,
        GisConversionContext context)
    {
        // Group all elements by their resolved feature class label.
        // Elements that were not classified will be grouped by their original source layer name.
        var groups = document.Layers
            .SelectMany(l => l.Elements.Select(e => (Element: e, LayerName: l.Name)))
            .GroupBy(t => classification.TryGetValue(t.Element.Id, out var result) ? result.Label : t.LayerName)
            .ToList();

        var virtualLayers = new List<SourceLayer>();
        foreach (var group in groups)
        {
            SourceLayer virtualLayer = new(group.Key);
            virtualLayer.AddElements(group.Select(t => t.Element));
            virtualLayers.Add(virtualLayer);
        }
        return virtualLayers;
    }

    private GeometryKind DominantGeometry(IReadOnlyList<SourceElement> elements)
    {
        Dictionary<GeometryKind, int> profile = [];

        foreach (SourceElement element in elements)
        {
            var kind = _geometryMapper.Classify(element.Geometry);
            profile[kind] = profile.GetValueOrDefault(kind) + 1;
        }

        GeometryKind dominant = GeometryKind.Unknown;
        int best = -1;

        foreach (KeyValuePair<GeometryKind, int> pair in profile)
        {
            if (pair.Value > best && pair.Key != GeometryKind.Unknown)
            {
                best = pair.Value;
                dominant = pair.Key;
            }
        }

        return dominant == GeometryKind.Unknown ? GeometryKind.Point : dominant;
    }

    private Result<CoordinateSystem> ResolveTarget(ConversionProfile profile, StreamingExportRequest request)
    {
        string? identifier = request.TargetCrsOverride ?? profile.OutputCrs;

        return string.IsNullOrWhiteSpace(identifier)
            ? Result.Success(request.SourceCoordinateSystem)
            : _crsRegistry.Resolve(identifier);
    }

    private Result<IStreamingExporter> ResolveExporter(ConversionProfile profile, StreamingExportRequest request)
    {
        if (!string.IsNullOrWhiteSpace(request.FormatOverride))
        {
            return _exporters.Resolve(request.FormatOverride);
        }

        return profile.ExportFormat is { } format
            ? _exporters.Resolve(format)
            : _exporters.Resolve(ExportFormat.GeoJson);
    }
}

/// <summary>What a streaming conversion is being asked to produce.</summary>
/// <param name="OutputDirectory">Folder the layers are written into.</param>
/// <param name="SourceCoordinateSystem">The system the source coordinates are in.</param>
/// <param name="RunId">The run these findings belong to.</param>
/// <param name="ProfileId">Profile to apply, or null for the configured default.</param>
/// <param name="TargetCrsOverride">Output system, overriding the profile.</param>
/// <param name="FormatOverride">Export format key, overriding the profile.</param>
public sealed record StreamingExportRequest(
    string OutputDirectory,
    CoordinateSystem SourceCoordinateSystem,
    ConversionRunId RunId,
    string? ProfileId = null,
    string? TargetCrsOverride = null,
    string? FormatOverride = null);

/// <summary>The result of a streaming conversion.</summary>
/// <param name="OutputPaths">Every file written.</param>
/// <param name="Report">The QA report.</param>
/// <param name="FeaturesWritten">Features successfully written.</param>
/// <param name="FeaturesSkipped">Features dropped.</param>
/// <param name="GeometriesRepaired">Geometries repaired on the way through.</param>
/// <param name="CoordinateSystem">The system the output is in.</param>
public sealed record GisConversionOutcome(
    IReadOnlyList<string> OutputPaths,
    ValidationReport Report,
    int FeaturesWritten,
    int FeaturesSkipped,
    int GeometriesRepaired,
    CoordinateSystem CoordinateSystem);
