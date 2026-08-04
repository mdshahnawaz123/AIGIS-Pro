using System.Runtime.CompilerServices;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using NetTopologySuite.Precision;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Features;

/// <summary>
/// Default <see cref="IFeatureBuilder"/>: the per-feature pipeline.
/// </summary>
/// <remarks>
/// <para>
/// Yields features one at a time and never accumulates. A layer of a million elements passes
/// through with a working set proportional to the largest single geometry, not to the layer.
/// </para>
/// <para>
/// Stage order is deliberate. Mapping and repair happen in source coordinates, because tolerances
/// are expressed in source units and a repair judged against a metre threshold means nothing once
/// the data is in degrees. Reprojection comes next, then precision snapping last, so the snapped
/// grid is the one the output is actually written on.
/// </para>
/// <para>
/// A run of consecutive failures aborts. A drawing where every feature fails is a different
/// problem from one with a handful of bad polygons, and should stop early rather than spend an
/// hour producing an empty file.
/// </para>
/// </remarks>
public sealed class FeatureBuilder : IFeatureBuilder
{
    // A single shared factory is enough for empty geometries: they carry no coordinates, so the
    // precision model and SRID are immaterial. Reused rather than allocated per skipped element.
    private static readonly GeometryFactory EmptyGeometryFactory = new();

    private readonly IGeometryMapper _mapper;
    private readonly IGeometryValidator _validator;
    private readonly IGeometryRepairer _repairer;
    private readonly IGeometrySimplifier _simplifier;
    private readonly IAttributeMapper _attributeMapper;
    private readonly Domain.Abstractions.Services.ICoordinateTransformer _transformer;
    private readonly IOptionsMonitor<GisOptions> _options;
    private readonly ILogger<FeatureBuilder> _logger;

    /// <summary>Initializes a new instance of the <see cref="FeatureBuilder"/> class.</summary>
    /// <param name="mapper">Normalises geometry for the target format.</param>
    /// <param name="validator">Inspects geometry against the profile's rules.</param>
    /// <param name="repairer">Repairs invalid geometry.</param>
    /// <param name="simplifier">Reduces vertex count when the profile asks.</param>
    /// <param name="attributeMapper">Maps attributes onto the schema.</param>
    /// <param name="transformer">Reprojects geometry.</param>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the builder.</param>
    public FeatureBuilder(
        IGeometryMapper mapper,
        IGeometryValidator validator,
        IGeometryRepairer repairer,
        IGeometrySimplifier simplifier,
        IAttributeMapper attributeMapper,
        Domain.Abstractions.Services.ICoordinateTransformer transformer,
        IOptionsMonitor<GisOptions> options,
        ILogger<FeatureBuilder> logger)
    {
        ArgumentNullException.ThrowIfNull(mapper);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(repairer);
        ArgumentNullException.ThrowIfNull(simplifier);
        ArgumentNullException.ThrowIfNull(attributeMapper);
        ArgumentNullException.ThrowIfNull(transformer);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _mapper = mapper;
        _validator = validator;
        _repairer = repairer;
        _simplifier = simplifier;
        _attributeMapper = attributeMapper;
        _transformer = transformer;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<GisFeature> BuildAsync(
        SourceLayer layer,
        FeatureClass featureClass,
        GisAttributeSchema schema,
        GisConversionContext context,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(featureClass);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(context);

        GisOptions options = _options.CurrentValue;
        PrecisionModel? precision = BuildPrecisionModel(context, options);
        int consecutiveFailures = 0;

        foreach (SourceElement element in layer.Elements)
        {
            cancellationToken.ThrowIfCancellationRequested();

            GisFeature? feature = BuildOne(element, layer, featureClass, schema, context, options, precision);

            if (feature is null)
            {
                context.CountSkipped();

                if (++consecutiveFailures >= options.Streaming.MaxConsecutiveFailures)
                {
                    _logger.LogError(
                        "Abandoning layer {Layer} after {Count} consecutive feature failures.",
                        layer.Name,
                        consecutiveFailures);

                    context.Record(ValidationIssue.Create(
                        IssueSeverity.Critical,
                        IssueCategory.Geometry,
                        "Gis.TooManyFailures",
                        $"Layer '{layer.Name}' was abandoned after {consecutiveFailures} consecutive failures.")
                        .ForLayer(LayerName.Create(layer.Name)));

                    yield break;
                }

                continue;
            }

            consecutiveFailures = 0;
            context.CountWritten();

            yield return feature;

            // Give the scheduler a chance so a synchronous source cannot starve the writer or
            // block cancellation on a very large layer.
            if (context.FeaturesWritten % options.Streaming.ValidationBatchSize == 0)
            {
                await Task.Yield();
            }
        }
    }

    private GisFeature? BuildOne(
        SourceElement element,
        SourceLayer layer,
        FeatureClass featureClass,
        GisAttributeSchema schema,
        GisConversionContext context,
        GisOptions options,
        PrecisionModel? precision)
    {
        NtsGeometry? geometry = element.Geometry;

        context.Record(_validator.Validate(geometry, element.Id, context.Profile.Qa));

        // Geometry that is usable on entry is run through the geometry stages. Anything null or
        // empty on entry is folded into the same "no usable geometry" case as a geometry the
        // pipeline tried and failed to build, so both are resolved by the single decision below.
        geometry = geometry is not null && !geometry.IsEmpty
            ? Process(geometry, element.Id, context, options, precision)
            : null;

        // H1: a feature must never leave this method carrying a null geometry. When no usable
        // geometry survives, either the element is dropped - when the profile is configured to do
        // so - or it is exported with a valid, explicit empty geometry of the feature class's
        // family. The empty geometry is non-null and IsValid, which is what the OGR-backed
        // Shapefile and GeoPackage writers require; keeping the feature also preserves the feature
        // count. DropIrreparableGeometry keeps its existing meaning: opt in to discard instead.
        if (geometry is null || geometry.IsEmpty)
        {
            if (options.Geometry.DropIrreparableGeometry)
            {
                _logger.LogDebug(
                    "Element {ElementId} on layer {Layer} produced no usable geometry and was dropped.",
                    element.Id,
                    layer.Name);

                return null;
            }

            geometry = CreateEmptyGeometry(featureClass.Geometry);

            _logger.LogDebug(
                "Element {ElementId} on layer {Layer} produced no usable geometry; exported with an "
                + "empty {Kind} geometry so the feature is never null.",
                element.Id,
                layer.Name,
                featureClass.Geometry);
        }

        var attrs = _attributeMapper.Map(element, schema, context.Profile);
        var semanticFeature = context.SemanticGraph?.GetFeature(element.Id);
        
        // Include some semantic fields natively if SemanticFeature exists
        if (semanticFeature != null)
        {
            var updatedAttrs = new Dictionary<string, AttributeValue>(attrs, StringComparer.OrdinalIgnoreCase);
            
            if (!string.IsNullOrEmpty(semanticFeature.Category))
            {
                updatedAttrs["SemanticCategory"] = AttributeValue.FromText(semanticFeature.Category);
            }
            
            if (!string.IsNullOrEmpty(semanticFeature.Family))
            {
                updatedAttrs["SemanticFamily"] = AttributeValue.FromText(semanticFeature.Family);
            }
                
            if (!string.IsNullOrEmpty(semanticFeature.Level))
            {
                updatedAttrs["SemanticLevel"] = AttributeValue.FromText(semanticFeature.Level);
            }
                
            attrs = updatedAttrs;
        }

        return new GisFeature(
            element.Id,
            featureClass,
            geometry,
            attrs,
            LayerName.Create(layer.Name),
            element.Id)
            {
                SemanticFeature = semanticFeature
            };
    }

    /// <summary>
    /// Creates a valid, empty geometry of the family the feature class expects.
    /// </summary>
    /// <remarks>
    /// An empty geometry reports <see cref="NtsGeometry.IsValid"/> as true and is never null, so it
    /// satisfies exporters that reject null geometry - notably the OGR-backed Shapefile and
    /// GeoPackage writers - while recording truthfully that the element carried no usable shape. The
    /// GeoJSON writer already renders an empty geometry as a JSON <c>null</c> member, which RFC 7946
    /// permits, so no exporter needs to change.
    /// </remarks>
    /// <param name="kind">The geometry family the feature class holds.</param>
    /// <returns>A non-null, empty geometry of the matching family.</returns>
    private static NtsGeometry CreateEmptyGeometry(GeometryKind kind) => kind switch
    {
        GeometryKind.Point or GeometryKind.Annotation => EmptyGeometryFactory.CreatePoint(),
        GeometryKind.Line => EmptyGeometryFactory.CreateLineString(),
        GeometryKind.Polygon => EmptyGeometryFactory.CreatePolygon(),
        _ => EmptyGeometryFactory.CreateGeometryCollection(),
    };

    /// <summary>Runs the geometry stages in order, returning null when the geometry is unusable.</summary>
    private NtsGeometry? Process(
        NtsGeometry geometry,
        string featureId,
        GisConversionContext context,
        GisOptions options,
        PrecisionModel? precision)
    {
        if (!geometry.IsValid && options.Geometry.RepairInvalidGeometry)
        {
            GeometryRepairResult repair = _repairer.Repair(geometry);

            if (!repair.Succeeded || repair.Geometry is null)
            {
                context.Record(ValidationIssue.Create(
                    IssueSeverity.Error,
                    IssueCategory.Geometry,
                    "Gis.RepairFailed",
                    $"The geometry is invalid and could not be repaired ({repair.Action}).")
                    .ForFeature(featureId));

                return null;
            }

            geometry = repair.Geometry;
            context.CountRepaired();

            // A repair that moves the area has changed the surveyed fact, not just its encoding.
            if (repair.AreaChangeRatio > 0.01d)
            {
                context.Record(ValidationIssue.Create(
                    IssueSeverity.Warning,
                    IssueCategory.Geometry,
                    "Gis.RepairChangedArea",
                    $"Repair by {repair.Action} changed the area by {repair.AreaChangeRatio:P1}.")
                    .ForFeature(featureId));
            }
        }

        Result<NtsGeometry> mapped = _mapper.Map(geometry, context.Profile.Geometry);

        if (mapped.IsFailure)
        {
            context.Record(ValidationIssue.Create(
                IssueSeverity.Error,
                IssueCategory.Geometry,
                mapped.Error.Code,
                mapped.Error.Message).ForFeature(featureId));

            return null;
        }

        geometry = mapped.Value;

        double simplification = context.Profile.SimplificationTolerance ?? options.Geometry.SimplificationTolerance;

        if (simplification > 0d)
        {
            geometry = _simplifier.Simplify(geometry, simplification);
        }

        if (context.RequiresTransformation)
        {
            Result<NtsGeometry> transformed = _transformer.Transform(geometry, context.SourceCrs, context.TargetCrs);

            if (transformed.IsFailure)
            {
                context.Record(ValidationIssue.Create(
                    IssueSeverity.Critical,
                    IssueCategory.Crs,
                    transformed.Error.Code,
                    transformed.Error.Message).ForFeature(featureId));

                return null;
            }

            geometry = transformed.Value;
        }

        if (precision is not null)
        {
            geometry = new GeometryPrecisionReducer(precision) { ChangePrecisionModel = true }.Reduce(geometry);
        }

        return geometry.IsEmpty ? null : geometry;
    }

    /// <summary>
    /// Builds the output precision grid, preferring the profile's value over the global default.
    /// </summary>
    /// <remarks>
    /// Snapping is what makes repeated conversions of the same drawing produce byte-identical
    /// output, which is the difference between a diffable deliverable and an unreviewable one.
    /// </remarks>
    private static PrecisionModel? BuildPrecisionModel(GisConversionContext context, GisOptions options)
    {
        double scale = context.Profile.PrecisionScale ?? options.Geometry.PrecisionScale;

        return scale > 0d ? new PrecisionModel(scale) : null;
    }
}
