using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Exceptions;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Entities.Project;

/// <summary>
/// The record of one execution of a job. Aggregate root.
/// </summary>
/// <remarks>
/// <para>
/// A separate aggregate from the project, because runs are queried on their own axis &#8212;
/// "what failed last night?" &#8212; and accumulate without bound, while a project is a small,
/// long-lived object. Loading six months of run history to rename a project would be absurd.
/// </para>
/// <para>
/// The run captures the settings it executed under, so a result stays explicable after the
/// project's settings have moved on.
/// </para>
/// </remarks>
public sealed class ConversionRun : Entity<ConversionRunId>, IAggregateRoot
{
    private readonly List<string> _outputPaths = [];

    private ConversionRun(
        ConversionRunId id,
        ConversionJobId jobId,
        ProjectId projectId,
        ConversionSettings settings)
        : base(id)
    {
        JobId = jobId;
        ProjectId = projectId;
        Settings = settings;
        Status = ConversionStatus.Queued;
    }

    /// <summary>Gets the job that was executed.</summary>
    public ConversionJobId JobId { get; }

    /// <summary>Gets the owning project.</summary>
    public ProjectId ProjectId { get; }

    /// <summary>Gets the settings in force when the run started.</summary>
    public ConversionSettings Settings { get; }

    /// <summary>Gets the current status.</summary>
    public ConversionStatus Status { get; private set; }

    /// <summary>Gets the instant the run started.</summary>
    public DateTimeOffset? StartedAtUtc { get; private set; }

    /// <summary>Gets the instant the run finished.</summary>
    public DateTimeOffset? FinishedAtUtc { get; private set; }

    /// <summary>Gets the wall-clock duration, or null while the run is unfinished.</summary>
    public TimeSpan? Duration => StartedAtUtc is null || FinishedAtUtc is null
        ? null
        : FinishedAtUtc.Value - StartedAtUtc.Value;

    /// <summary>Gets the coordinate system the run resolved.</summary>
    public CoordinateSystem? ResolvedCoordinateSystem { get; private set; }

    /// <summary>Gets how the coordinate system was determined.</summary>
    public CrsDetectionSource CrsSource { get; private set; } = CrsDetectionSource.None;

    /// <summary>Gets the number of source elements read.</summary>
    public int ElementsRead { get; private set; }

    /// <summary>Gets the number of features written.</summary>
    public int FeaturesWritten { get; private set; }

    /// <summary>Gets the most serious validation finding recorded.</summary>
    public IssueSeverity HighestSeverity { get; private set; } = IssueSeverity.Information;

    /// <summary>Gets the number of validation findings recorded.</summary>
    public int IssueCount { get; private set; }

    /// <summary>Gets the reason the run failed, when it did.</summary>
    public string? FailureReason { get; private set; }

    /// <summary>Gets the files the run wrote.</summary>
    public IReadOnlyList<string> OutputPaths => _outputPaths;

    /// <summary>Creates a run for a job.</summary>
    /// <param name="job">The job to execute.</param>
    /// <param name="settings">The settings in force.</param>
    /// <returns>The created run.</returns>
    public static ConversionRun Create(ConversionJob job, ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(settings);

        return new ConversionRun(ConversionRunId.New(), job.Id, job.ProjectId, settings);
    }

    /// <summary>Marks the run as started.</summary>
    /// <exception cref="InvalidConversionStateException">The run was not queued.</exception>
    public void Start()
    {
        if (Status != ConversionStatus.Queued)
        {
            throw InvalidConversionStateException.For(Status, "start the run");
        }

        Status = ConversionStatus.Running;
        StartedAtUtc = DateTimeOffset.UtcNow;

        Raise(new Events.ConversionRunStarted(Id, JobId));
    }

    /// <summary>Records the coordinate system the run will use.</summary>
    /// <param name="coordinateSystem">The resolved system.</param>
    /// <param name="source">How it was determined.</param>
    public void RecordCoordinateSystem(CoordinateSystem coordinateSystem, CrsDetectionSource source)
    {
        ArgumentNullException.ThrowIfNull(coordinateSystem);
        RequireRunning("record the coordinate system");

        ResolvedCoordinateSystem = coordinateSystem;
        CrsSource = source;

        Raise(new Events.CoordinateSystemDetermined(Id, coordinateSystem, source));
    }

    /// <summary>Records how much was read from the source.</summary>
    /// <param name="elementsRead">The number of source elements read.</param>
    public void RecordSourceRead(int elementsRead)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(elementsRead);
        RequireRunning("record the source read");

        ElementsRead = elementsRead;
    }

    /// <summary>Records the outcome of classification.</summary>
    /// <param name="accepted">Layers classified at or above the threshold.</param>
    /// <param name="belowThreshold">Layers classified but needing review.</param>
    /// <param name="unclassified">Layers no provider could classify.</param>
    public void RecordClassification(int accepted, int belowThreshold, int unclassified)
    {
        RequireRunning("record classification");

        Raise(new Events.LayersClassified(Id, accepted, belowThreshold, unclassified));
    }

    /// <summary>Records the outcome of validation.</summary>
    /// <param name="highestSeverity">The most serious finding.</param>
    /// <param name="issueCount">Total findings.</param>
    public void RecordValidation(IssueSeverity highestSeverity, int issueCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(issueCount);
        RequireRunning("record validation");

        HighestSeverity = highestSeverity;
        IssueCount = issueCount;

        Raise(new Events.ValidationCompleted(Id, highestSeverity, issueCount));
    }

    /// <summary>Records a file the run wrote.</summary>
    /// <param name="path">The output path.</param>
    public void RecordOutput(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        RequireRunning("record an output");

        _outputPaths.Add(path);
    }

    /// <summary>
    /// Completes the run successfully.
    /// </summary>
    /// <remarks>
    /// The terminal status is derived, not supplied: a run with error-level findings reports
    /// <see cref="ConversionStatus.SucceededWithWarnings"/> whatever the caller believes, so a
    /// batch summary cannot claim a clean result over data that needs review.
    /// </remarks>
    /// <param name="featuresWritten">The number of features written.</param>
    public void Complete(int featuresWritten)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(featuresWritten);
        RequireRunning("complete the run");

        FeaturesWritten = featuresWritten;
        Status = HighestSeverity >= IssueSeverity.Warning
            ? ConversionStatus.SucceededWithWarnings
            : ConversionStatus.Succeeded;

        Finish();
    }

    /// <summary>Fails the run.</summary>
    /// <param name="reason">Why the run failed.</param>
    public void Fail(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (Status is ConversionStatus.Succeeded or ConversionStatus.SucceededWithWarnings)
        {
            throw InvalidConversionStateException.For(Status, "fail the run");
        }

        FailureReason = reason;
        Status = ConversionStatus.Failed;

        Finish();
    }

    /// <summary>Cancels the run at the operator's request.</summary>
    public void Cancel()
    {
        if (Status is not (ConversionStatus.Queued or ConversionStatus.Running))
        {
            throw InvalidConversionStateException.For(Status, "cancel the run");
        }

        Status = ConversionStatus.Cancelled;
        Finish();
    }

    private void Finish()
    {
        FinishedAtUtc = DateTimeOffset.UtcNow;
        StartedAtUtc ??= FinishedAtUtc;

        Raise(new Events.ConversionRunFinished(
            Id,
            JobId,
            Status,
            Duration ?? TimeSpan.Zero,
            FeaturesWritten));
    }

    private void RequireRunning(string operation)
    {
        if (Status != ConversionStatus.Running)
        {
            throw InvalidConversionStateException.For(Status, operation);
        }
    }
}
