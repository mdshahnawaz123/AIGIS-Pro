using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Pipelines;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Enums;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Services.Conversion;

/// <summary>Converts one drawing and records what happened.</summary>
public interface IConversionService
{
    /// <summary>Converts a single job.</summary>
    /// <param name="job">The job to run.</param>
    /// <param name="settings">The settings in force.</param>
    /// <param name="outputDirectory">Where the outputs are written.</param>
    /// <param name="profileId">Conversion profile to apply, or null for the default.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the conversion.</param>
    /// <returns>The completed run.</returns>
    Task<Result<ConversionRun>> ConvertAsync(
        ConversionJob job,
        ConversionSettingsSnapshot settings,
        string outputDirectory,
        string? profileId = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The settings a run executes under, captured at the moment it starts.</summary>
/// <param name="Settings">The project's settings.</param>
public sealed record ConversionSettingsSnapshot(Domain.ValueObjects.ConversionSettings Settings);

/// <summary>
/// Default <see cref="IConversionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Owns the lifecycle of a run and nothing about conversion itself. It creates the record, starts
/// it, hands the work to the pipeline, and closes the record with whatever came back &#8212;
/// including when the pipeline throws, because a run left in <c>Running</c> forever is worse than
/// one recorded as failed.
/// </para>
/// <para>
/// The terminal status is derived by the aggregate, not chosen here: a run with error-level
/// findings reports success-with-warnings whatever this service believes, so a batch summary
/// cannot claim a clean result over data that needs review.
/// </para>
/// </remarks>
public sealed class ConversionService : IConversionService
{
    private readonly IConversionPipeline _pipeline;
    private readonly IConversionRunRepository _runs;
    private readonly IValidationReportRepository _reports;
    private readonly IUnitOfWork _unitOfWork;
    private readonly INotificationService _notifications;
    private readonly ILogger<ConversionService> _logger;

    /// <summary>Initializes a new instance of the <see cref="ConversionService"/> class.</summary>
    /// <param name="pipeline">The conversion pipeline.</param>
    /// <param name="runs">Run persistence.</param>
    /// <param name="reports">Validation report persistence.</param>
    /// <param name="unitOfWork">The transaction boundary.</param>
    /// <param name="notifications">Tells the operator what happened.</param>
    /// <param name="logger">Logger for the service.</param>
    public ConversionService(
        IConversionPipeline pipeline,
        IConversionRunRepository runs,
        IValidationReportRepository reports,
        IUnitOfWork unitOfWork,
        INotificationService notifications,
        ILogger<ConversionService> logger)
    {
        ArgumentNullException.ThrowIfNull(pipeline);
        ArgumentNullException.ThrowIfNull(runs);
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(unitOfWork);
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(logger);

        _pipeline = pipeline;
        _runs = runs;
        _reports = reports;
        _unitOfWork = unitOfWork;
        _notifications = notifications;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<ConversionRun>> ConvertAsync(
        ConversionJob job,
        ConversionSettingsSnapshot settings,
        string outputDirectory,
        string? profileId = null,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        ConversionRun run = ConversionRun.Create(job, settings.Settings);
        await _runs.AddAsync(run, cancellationToken).ConfigureAwait(false);

        run.Start();

        PipelineContext context = new(job.Source, settings.Settings, run, outputDirectory)
        {
            ProfileId = profileId,
        };

        try
        {
            Result outcome = await _pipeline.ExecuteAsync(context, progress, cancellationToken)
                .ConfigureAwait(false);

            if (outcome.IsFailure)
            {
                run.Fail(outcome.Error.Message);
                await PersistAsync(context, cancellationToken).ConfigureAwait(false);

                _notifications.Publish(new Notification(
                    NotificationLevel.Error,
                    $"Conversion failed: {Path.GetFileName(job.Source.Location)}",
                    outcome.Error.Message));

                return Result.Failure<ConversionRun>(outcome.Error);
            }

            run.Complete(context.Datasets.Sum(static dataset => dataset.Features.Count));
            await PersistAsync(context, cancellationToken).ConfigureAwait(false);

            Notify(job, run, context);

            return Result.Success(run);
        }
        catch (OperationCanceledException)
        {
            run.Cancel();
            await PersistAsync(context, CancellationToken.None).ConfigureAwait(false);

            _notifications.Publish(new Notification(
                NotificationLevel.Warning,
                $"Conversion cancelled: {Path.GetFileName(job.Source.Location)}"));

            throw;
        }
        catch (Exception ex)
        {
            // A run left Running forever is worse than one recorded as failed.
            _logger.LogError(ex, "Conversion of {Location} failed unexpectedly.", job.Source.Location);

            run.Fail(ex.Message);
            await PersistAsync(context, CancellationToken.None).ConfigureAwait(false);

            return Result.Failure<ConversionRun>(new Error("Conversion.Unexpected", ex.Message));
        }
    }

    private async Task PersistAsync(PipelineContext context, CancellationToken cancellationToken)
    {
        if (context.Report is not null)
        {
            await _reports.AddAsync(context.Report, cancellationToken).ConfigureAwait(false);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private void Notify(ConversionJob job, ConversionRun run, PipelineContext context)
    {
        string file = Path.GetFileName(job.Source.Location);

        if (run.Status == ConversionStatus.SucceededWithWarnings || context.DegradedStages.Count > 0)
        {
            string detail = context.DegradedStages.Count > 0
                ? $"{run.IssueCount} findings. Skipped: {string.Join(", ", context.DegradedStages)}."
                : $"{run.IssueCount} findings, highest severity {run.HighestSeverity}.";

            _notifications.Publish(new Notification(
                NotificationLevel.Warning,
                $"Converted with warnings: {file}",
                detail));

            return;
        }

        _notifications.Publish(new Notification(
            NotificationLevel.Information,
            $"Converted: {file}",
            $"{run.FeaturesWritten:N0} features written to {context.OutputPaths.Count} files."));
    }
}
