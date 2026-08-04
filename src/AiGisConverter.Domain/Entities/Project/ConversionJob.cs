using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Exceptions;

namespace AiGisConverter.Domain.Entities.Project;

/// <summary>
/// One source to be converted. Lives inside a <see cref="ConversionProject"/>.
/// </summary>
/// <remarks>
/// A job is not an aggregate root: it is reached through its project, which is what lets the
/// project enforce rules spanning several jobs, such as refusing to queue an empty project.
/// </remarks>
public sealed class ConversionJob : Entity<ConversionJobId>
{
    internal ConversionJob(ConversionJobId id, ProjectId projectId, SourceReference source)
        : base(id)
    {
        ArgumentNullException.ThrowIfNull(source);

        ProjectId = projectId;
        Source = source;
        Status = ConversionStatus.Draft;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the owning project.</summary>
    public ProjectId ProjectId { get; }

    /// <summary>Gets the source this job reads.</summary>
    public SourceReference Source { get; }

    /// <summary>Gets the current status.</summary>
    public ConversionStatus Status { get; private set; }

    /// <summary>Gets the instant the job was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Gets the identifier of the most recent run, when the job has been executed.</summary>
    public ConversionRunId? LatestRunId { get; private set; }

    /// <summary>Gets a value indicating whether the job has reached a terminal state.</summary>
    public bool IsTerminal => Status is ConversionStatus.Succeeded
        or ConversionStatus.SucceededWithWarnings
        or ConversionStatus.Failed
        or ConversionStatus.Cancelled;

    /// <summary>Marks the job as queued.</summary>
    /// <exception cref="InvalidConversionStateException">The job is already queued or running.</exception>
    internal void Queue()
    {
        if (Status is ConversionStatus.Queued or ConversionStatus.Running)
        {
            throw InvalidConversionStateException.For(Status, "queue the job");
        }

        Status = ConversionStatus.Queued;
    }

    /// <summary>Marks the job as running under a given run.</summary>
    /// <param name="runId">The run now executing this job.</param>
    /// <exception cref="InvalidConversionStateException">The job was not queued.</exception>
    internal void MarkRunning(ConversionRunId runId)
    {
        if (Status != ConversionStatus.Queued)
        {
            throw InvalidConversionStateException.For(Status, "start the job");
        }

        Status = ConversionStatus.Running;
        LatestRunId = runId;
    }

    /// <summary>Records the terminal outcome of the job.</summary>
    /// <param name="status">The terminal status.</param>
    /// <exception cref="InvalidConversionStateException">The status supplied is not terminal.</exception>
    internal void Complete(ConversionStatus status)
    {
        if (status is not (ConversionStatus.Succeeded
            or ConversionStatus.SucceededWithWarnings
            or ConversionStatus.Failed
            or ConversionStatus.Cancelled))
        {
            throw new InvalidConversionStateException($"'{status}' is not a terminal status.");
        }

        Status = status;
    }
}
