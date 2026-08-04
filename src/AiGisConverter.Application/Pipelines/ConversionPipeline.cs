using System.Diagnostics;
using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Common;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Pipelines;

/// <summary>Runs a conversion.</summary>
public interface IConversionPipeline
{
    /// <summary>Gets the stages, in execution order.</summary>
    IReadOnlyList<IPipelineStage> Stages { get; }

    /// <summary>Runs every stage against a context.</summary>
    /// <param name="context">The conversion to run.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>Success when the required stages completed, or the first blocking failure.</returns>
    Task<Result> ExecuteAsync(
        PipelineContext context,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Default <see cref="IConversionPipeline"/>.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline owns ordering and error recovery, and nothing else. Every stage delegates its
/// decision to the module that owns it, so this class has no opinion about tolerances, thresholds
/// or what counts as valid geometry &#8212; only about what to do when a stage cannot finish.
/// </para>
/// <para>
/// A failing optional stage degrades the run and is recorded; a failing required stage stops it.
/// That distinction is the whole of the error-recovery policy, and it is stated on the stage
/// rather than here so adding a stage does not mean editing a list of exceptions.
/// </para>
/// </remarks>
public sealed class ConversionPipeline : IConversionPipeline
{
    private readonly ILogger<ConversionPipeline> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConversionPipeline"/> class.</summary>
    /// <param name="stages">The registered stages, in any order.</param>
    /// <param name="logger">Logger for the pipeline.</param>
    public ConversionPipeline(IEnumerable<IPipelineStage> stages, ILogger<ConversionPipeline> logger)
    {
        ArgumentNullException.ThrowIfNull(stages);
        ArgumentNullException.ThrowIfNull(logger);

        Stages = [.. stages.OrderBy(static stage => stage.Order)];
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<IPipelineStage> Stages { get; }

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        PipelineContext context,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        for (int index = 0; index < Stages.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IPipelineStage stage = Stages[index];
            progress?.Report(new ConversionProgress(stage.Name, "Starting...", index, Stages.Count));

            long startedAt = Stopwatch.GetTimestamp();
            Result result = await RunStageAsync(stage, context, cancellationToken).ConfigureAwait(false);
            TimeSpan elapsed = Stopwatch.GetElapsedTime(startedAt);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Stage {Stage} completed in {ElapsedMs} ms.",
                    stage.Name, elapsed.TotalMilliseconds);

                progress?.Report(new ConversionProgress(stage.Name, "Done", index, Stages.Count, 1d));
                continue;
            }

            if (!stage.IsOptional)
            {
                _logger.LogError("Stage {Stage} failed after {ElapsedMs} ms: {Reason}",
                    stage.Name, elapsed.TotalMilliseconds, result.Error.Message);

                return result;
            }

            context.DegradedStages.Add(stage.Name);

            _logger.LogWarning(
                "Optional stage {Stage} failed after {ElapsedMs} ms and was skipped: {Reason}",
                stage.Name,
                elapsed.TotalMilliseconds,
                result.Error.Message);

            progress?.Report(new ConversionProgress(
                stage.Name, $"Skipped: {result.Error.Message}", index, Stages.Count, 1d));
        }

        return Result.Success();
    }

    /// <summary>
    /// Runs a stage, converting an unexpected exception into a failure.
    /// </summary>
    /// <remarks>
    /// Stages are expected to return failures rather than throw, but a plugin-contributed reader
    /// or a vendor SDK is not this codebase and will throw eventually. Containing it here means a
    /// misbehaving stage degrades or stops the run cleanly instead of unwinding through the batch
    /// service and taking the other files with it.
    /// </remarks>
    private async Task<Result> RunStageAsync(
        IPipelineStage stage,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        try
        {
            return await stage.ExecuteAsync(context, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Stage {Stage} threw.", stage.Name);

            return Result.Failure(new Error(
                "Pipeline.StageThrew",
                $"Stage '{stage.Name}' failed unexpectedly: {ex.Message}"));
        }
    }
}
