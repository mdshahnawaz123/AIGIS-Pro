using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Pipelines;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Application.Pipelines.Steps;

/// <summary>
/// Publishes the finished conversion to the shared <see cref="IConversionSession"/>.
/// </summary>
/// <remarks>
/// The last stage, and optional: it makes the result available to screens that show "the current
/// drawing" (the Mapping Editor). A failure here must never fail a conversion that has already
/// written its output, so the drawing is still converted even if publishing throws.
/// </remarks>
public sealed class PublishSessionStage : IPipelineStage
{
    private readonly IConversionSession _session;
    private readonly ILogger<PublishSessionStage> _logger;

    /// <summary>Initializes a new instance of the <see cref="PublishSessionStage"/> class.</summary>
    /// <param name="session">The shared conversion session to publish into.</param>
    /// <param name="logger">Logger for the stage.</param>
    public PublishSessionStage(IConversionSession session, ILogger<PublishSessionStage> logger)
    {
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(logger);
        _session = session;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Publish session";

    /// <inheritdoc />
    public int Order => 700;

    /// <inheritdoc />
    public bool IsOptional => true;

    /// <inheritdoc />
    public Task<Result> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            IReadOnlyList<GisFeature> features = [.. context.Datasets.SelectMany(static d => d.Features)];

            Extent transformed = Extent.Empty;

            foreach (GisDataset dataset in context.Datasets)
            {
                transformed = transformed.Union(dataset.Extent);
            }

            IReadOnlyList<SessionLayer> layers = LayersOf(context.Document);
            IReadOnlyList<SessionEntityType> entityTypes = EntityTypesOf(context.Document);
            Extent sourceExtent = SourceExtentOf(context.Document);

            ConversionSessionSnapshot snapshot = new(
                Path.GetFileName(context.Source.Location),
                context.Source.Location,
                context.SourceCoordinateSystem,
                context.Settings.TargetCoordinateSystem,
                sourceExtent,
                transformed,
                features,
                layers,
                entityTypes,
                context.Report?.Issues ?? [],
                BuildSummary(context, features.Count, layers, entityTypes, transformed),
                EntityTypeIndexOf(context.Document));

            _session.Publish(snapshot);
        }
        catch (Exception ex)
        {
            // Optional stage: the conversion already succeeded, so a publishing failure is logged
            // and swallowed rather than reported as a conversion error.
            _logger.LogWarning(ex, "The conversion result could not be published to the session.");
        }

        return Task.FromResult(Result.Success());
    }

    /// <summary>Builds the record of how this conversion was actually performed.</summary>
    private static ConversionSummary BuildSummary(
        PipelineContext context,
        int featureCount,
        IReadOnlyList<SessionLayer> layers,
        IReadOnlyList<SessionEntityType> entityTypes,
        Extent transformed)
    {
        CoordinateSystem? input = context.SourceCoordinateSystem;
        CoordinateSystem output = context.Settings.TargetCoordinateSystem;

        bool reprojected = input is not null && input != output;

        string method = input is null
            ? "None — the input coordinate system was not determined."
            : reprojected
                ? $"Reprojected {input.Identifier} → {output.Identifier}"
                : $"No reprojection — already in {output.Identifier}";

        // Confidence follows provenance: a system read from the drawing is trustworthy, one the
        // operator assumed is only as good as their knowledge, and none at all is zero.
        double confidence = context.CrsSource switch
        {
            // Read from the drawing itself: authoritative.
            CrsDetectionSource.PrjSidecar or CrsDetectionSource.EmbeddedGeoData
                or CrsDetectionSource.VendorExtendedData => 0.95d,

            // Chosen by the operator: as good as their knowledge of the survey.
            CrsDetectionSource.UserSupplied => 0.85d,

            // Inferred from coordinate magnitudes, or simply the configured fallback.
            CrsDetectionSource.ExtentHeuristic => 0.50d,
            CrsDetectionSource.ApplicationDefault => 0.30d,

            _ => 0d,
        };

        return new ConversionSummary(
            DateTimeOffset.UtcNow,
            context.Run.ProjectId.ToString(),
            Path.GetFileName(context.Source.Location),
            input?.Identifier ?? "Not determined",
            output.Identifier,
            context.CrsSource.ToString(),
            context.Document?.Units ?? "Unknown",
            method,
            confidence,
            DescribeRegion(transformed, output),
            entityTypes.Sum(static type => type.Count),
            featureCount,
            layers.Count,
            context.Run.Duration ?? TimeSpan.Zero,
            string.Join(", ", context.Settings.ExportFormats),
            context.Report is null
                ? "Not validated"
                : $"{context.Report.TotalCount} finding(s), highest {context.Report.HighestSeverity}");
    }

    /// <summary>Describes where the transformed data sits, when the output is geographic.</summary>
    private static string DescribeRegion(Extent transformed, CoordinateSystem output)
    {
        if (transformed.IsEmpty || !output.IsGeographic)
        {
            return "-";
        }

        return $"~{transformed.CentreX:F2}°, {transformed.CentreY:F2}°";
    }

    /// <summary>Indexes each source element's native CAD type by its identifier.</summary>
    /// <param name="document">The document read from the drawing.</param>
    /// <returns>Element id to native type, for example <c>2A7</c> to <c>LWPOLYLINE</c>.</returns>
    private static IReadOnlyDictionary<string, string> EntityTypeIndexOf(SourceDocument? document)
    {
        Dictionary<string, string> index = new(StringComparer.OrdinalIgnoreCase);

        if (document is null)
        {
            return index;
        }

        foreach (SourceLayer layer in document.Layers)
        {
            foreach (SourceElement element in layer.Elements)
            {
                index[element.Id] = element.NativeType ?? element.GeometryKind.ToString();
            }
        }

        return index;
    }

    private static IReadOnlyList<SessionLayer> LayersOf(SourceDocument? document) =>
        document is null
            ? []
            : [.. document.Layers.Select(static layer => new SessionLayer(layer.Name, layer.Elements.Count))];

    private static IReadOnlyList<SessionEntityType> EntityTypesOf(SourceDocument? document)
    {
        if (document is null)
        {
            return [];
        }

        return [.. document.Layers
            .SelectMany(static layer => layer.Elements)
            .GroupBy(static element => element.NativeType ?? element.GeometryKind.ToString(), StringComparer.OrdinalIgnoreCase)
            .Select(static group => new SessionEntityType(group.Key, group.Count()))
            .OrderByDescending(static type => type.Count)];
    }

    private static Extent SourceExtentOf(SourceDocument? document)
    {
        if (document is null)
        {
            return Extent.Empty;
        }

        Extent extent = Extent.Empty;

        foreach (SourceLayer layer in document.Layers)
        {
            foreach (SourceElement element in layer.Elements)
            {
                if (element.Geometry is NtsGeometry { IsEmpty: false } geometry)
                {
                    NetTopologySuite.Geometries.Envelope box = geometry.EnvelopeInternal;
                    extent = extent.Union(Extent.Create(box.MinX, box.MinY, box.MaxX, box.MaxY));
                }
            }
        }

        return extent;
    }
}
