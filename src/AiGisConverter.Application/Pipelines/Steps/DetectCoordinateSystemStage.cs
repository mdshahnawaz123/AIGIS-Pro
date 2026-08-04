using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Pipelines.Steps;

/// <summary>
/// Settles which coordinate system the source is in.
/// </summary>
/// <remarks>
/// The stage decides nothing: the detector owns the chain of strategies and reports which one
/// answered. All this does is record the answer on the run, so that when a survey lands in the
/// wrong place the question "how did we decide the CRS?" has a stored answer.
/// </remarks>
public sealed class DetectCoordinateSystemStage : IPipelineStage
{
    private readonly ICrsDetector _detector;
    private readonly ILogger<DetectCoordinateSystemStage> _logger;

    /// <summary>Initializes a new instance of the <see cref="DetectCoordinateSystemStage"/> class.</summary>
    /// <param name="detector">The detection chain.</param>
    /// <param name="logger">Logger for the stage.</param>
    public DetectCoordinateSystemStage(ICrsDetector detector, ILogger<DetectCoordinateSystemStage> logger)
    {
        ArgumentNullException.ThrowIfNull(detector);
        ArgumentNullException.ThrowIfNull(logger);

        _detector = detector;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Detect coordinate system";

    /// <inheritdoc />
    public int Order => 200;

    /// <inheritdoc />
    public bool IsOptional => false;

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Document is null)
        {
            return Result.Failure(new Error("Pipeline.NoDocument", "No document was read."));
        }

        Result<CrsDetectionResult> detection = await _detector
            .DetectAsync(context.Document, context.Settings.AssumedSourceCoordinateSystem, cancellationToken)
            .ConfigureAwait(false);

        if (detection.IsFailure)
        {
            return Result.Failure(detection.Error);
        }

        context.SourceCoordinateSystem = detection.Value.CoordinateSystem;
        context.CrsSource = detection.Value.Source;
        context.Run.RecordCoordinateSystem(detection.Value.CoordinateSystem, detection.Value.Source);

        _logger.LogInformation(
            "Source coordinate system {Crs} determined from {Source} (confidence {Confidence}).",
            detection.Value.CoordinateSystem.Identifier,
            detection.Value.Source,
            detection.Value.Confidence);

        return Result.Success();
    }
}
