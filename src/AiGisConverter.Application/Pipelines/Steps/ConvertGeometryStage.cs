using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Pipelines.Steps;

/// <summary>Turns the read document into export-ready datasets.</summary>
/// <remarks>
/// Tessellation, repair, reprojection and precision are all decided inside the GIS layer. This
/// stage supplies the two coordinate systems and the classification, and records what came back.
/// </remarks>
public sealed class ConvertGeometryStage : IPipelineStage
{
    private readonly IGeometryConverter _converter;
    private readonly ILogger<ConvertGeometryStage> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConvertGeometryStage"/> class.</summary>
    /// <param name="converter">The geometry conversion port.</param>
    /// <param name="logger">Logger for the stage.</param>
    public ConvertGeometryStage(IGeometryConverter converter, ILogger<ConvertGeometryStage> logger)
    {
        ArgumentNullException.ThrowIfNull(converter);
        ArgumentNullException.ThrowIfNull(logger);

        _converter = converter;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Convert geometry";

    /// <inheritdoc />
    public int Order => 400;

    /// <inheritdoc />
    public bool IsOptional => false;

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Document is null || context.SourceCoordinateSystem is null)
        {
            return Result.Failure(new Error(
                "Pipeline.NotReady",
                "Geometry conversion needs a read document and a resolved coordinate system."));
        }

        Result<IReadOnlyList<GisDataset>> converted = await _converter.ConvertAsync(
            context.Document,
            context.EntityClassifications,
            context.SourceCoordinateSystem,
            context.Settings.TargetCoordinateSystem,
            cancellationToken).ConfigureAwait(false);

        if (converted.IsFailure)
        {
            return Result.Failure(converted.Error);
        }

        context.Datasets = converted.Value;

        _logger.LogInformation(
            "Converted {LayerCount} layers into {DatasetCount} datasets holding {FeatureCount} features.",
            context.Document.Layers.Count,
            converted.Value.Count,
            converted.Value.Sum(static dataset => dataset.Features.Count));

        return Result.Success();
    }
}
