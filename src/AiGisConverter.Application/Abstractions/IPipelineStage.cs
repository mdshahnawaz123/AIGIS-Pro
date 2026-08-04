using AiGisConverter.Application.Pipelines;
using AiGisConverter.Domain.Common;

namespace AiGisConverter.Application.Abstractions;

/// <summary>
/// One step of the conversion pipeline.
/// </summary>
/// <remarks>
/// <para>
/// A stage decides nothing. It calls the module that owns the decision, puts the answer on the
/// context, and reports whether it could. Every threshold, tolerance and rule lives behind the
/// port a stage calls, which is what keeps this layer from acquiring a second, divergent copy of
/// the domain's judgement.
/// </para>
/// <para>
/// A stage is a class rather than a method so the pipeline's shape is data: stages can be
/// reordered, disabled, or added by a caller without editing the code that runs them.
/// </para>
/// </remarks>
public interface IPipelineStage
{
    /// <summary>Gets the stage name, used in progress messages and logs.</summary>
    string Name { get; }

    /// <summary>Gets the execution order. Lower runs first.</summary>
    int Order { get; }

    /// <summary>
    /// Gets a value indicating whether the pipeline continues when this stage fails.
    /// </summary>
    /// <remarks>
    /// Classification is optional: an unreachable model should leave every layer unclassified, not
    /// abandon a conversion the operator can still use. Reading the source is not.
    /// </remarks>
    bool IsOptional { get; }

    /// <summary>Runs the stage.</summary>
    /// <param name="context">The conversion in progress.</param>
    /// <param name="cancellationToken">Token used to cancel the stage.</param>
    /// <returns>Success, or a failure describing what could not be done.</returns>
    Task<Result> ExecuteAsync(PipelineContext context, CancellationToken cancellationToken = default);
}
