using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Pipelines.Steps;

/// <summary>
/// Runs the quality rules and records the outcome on the run.
/// </summary>
/// <remarks>
/// <para>
/// Optional as a stage, and consequential in its result. A QA engine that cannot run leaves the
/// conversion unvalidated rather than unfinished; findings that the engine does produce decide
/// whether the run reports success or success-with-warnings, and that decision belongs to the
/// report, not to this stage.
/// </para>
/// </remarks>
public sealed class ValidateStage : IPipelineStage
{
    private readonly IQaQcEngine _engine;
    private readonly IQaReportRenderer _renderer;
    private readonly ILogger<ValidateStage> _logger;

    /// <summary>Initializes a new instance of the <see cref="ValidateStage"/> class.</summary>
    /// <param name="engine">The rule engine.</param>
    /// <param name="renderer">Renders the report to disk.</param>
    /// <param name="logger">Logger for the stage.</param>
    public ValidateStage(IQaQcEngine engine, IQaReportRenderer renderer, ILogger<ValidateStage> logger)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(renderer);
        ArgumentNullException.ThrowIfNull(logger);

        _engine = engine;
        _renderer = renderer;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Validate";

    /// <inheritdoc />
    public int Order => 500;

    /// <inheritdoc />
    public bool IsOptional => true;

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Datasets.Count == 0)
        {
            return Result.Success();
        }

        Result<ValidationReport> validation = await _engine
            .ValidateAsync(context.Run.Id, context.Datasets, progress: null, cancellationToken)
            .ConfigureAwait(false);

        if (validation.IsFailure)
        {
            return Result.Failure(validation.Error);
        }

        context.Report = validation.Value;
        context.Run.RecordValidation(validation.Value.HighestSeverity, validation.Value.TotalCount);

        IReadOnlyList<string> rendered = await _renderer.RenderAsync(
            validation.Value,
            Path.Combine(context.OutputDirectory, "qa-report"),
            cancellationToken).ConfigureAwait(false);

        context.RecordOutputs(rendered);

        _logger.LogInformation(
            "Validation produced {IssueCount} findings, highest severity {Severity}.",
            validation.Value.TotalCount,
            validation.Value.HighestSeverity);

        return Result.Success();
    }
}
