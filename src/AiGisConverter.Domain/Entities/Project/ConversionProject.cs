using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Exceptions;
using AiGisConverter.Domain.Validation;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Entities.Project;

/// <summary>
/// A named set of sources converted together under one set of settings. Aggregate root.
/// </summary>
/// <remarks>
/// The project owns its jobs. They are created only through <see cref="AddJob"/> and exposed as a
/// read-only list, so the invariant that every job belongs to exactly one project, and that
/// queueing requires at least one job, cannot be bypassed by a caller holding the collection.
/// </remarks>
public sealed class ConversionProject : Entity<ProjectId>, IAggregateRoot, IValidatable
{
    private readonly List<ConversionJob> _jobs = [];

    private ConversionProject(ProjectId id, string name, ConversionSettings settings)
        : base(id)
    {
        Name = name;
        Settings = settings;
        CreatedAtUtc = DateTimeOffset.UtcNow;
    }

    /// <summary>Gets the project name.</summary>
    public string Name { get; private set; }

    /// <summary>Gets the optional description.</summary>
    public string? Description { get; private set; }

    /// <summary>Gets the settings every job in this project is converted under.</summary>
    public ConversionSettings Settings { get; private set; }

    /// <summary>Gets the instant the project was created.</summary>
    public DateTimeOffset CreatedAtUtc { get; }

    /// <summary>Gets the instant the project was last modified.</summary>
    public DateTimeOffset? ModifiedAtUtc { get; private set; }

    /// <summary>Gets the jobs in this project.</summary>
    public IReadOnlyList<ConversionJob> Jobs => _jobs;

    /// <summary>Creates a project.</summary>
    /// <param name="name">The project name.</param>
    /// <param name="settings">The conversion settings.</param>
    /// <returns>The created project.</returns>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    public static ConversionProject Create(string name, ConversionSettings settings)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(settings);

        ConversionProject project = new(ProjectId.New(), name.Trim(), settings);
        project.Raise(new Events.ConversionProjectCreated(project.Id, project.Name));

        return project;
    }

    /// <summary>Adds a source to the project.</summary>
    /// <param name="source">The source to convert.</param>
    /// <returns>The created job.</returns>
    /// <exception cref="DomainException">The same source is already present.</exception>
    public ConversionJob AddJob(SourceReference source)
    {
        ArgumentNullException.ThrowIfNull(source);

        Guard.Requires(
            !_jobs.Any(job => string.Equals(
                job.Source.Location,
                source.Location,
                StringComparison.OrdinalIgnoreCase)),
            $"'{source.Location}' is already part of this project.");

        ConversionJob job = new(ConversionJobId.New(), Id, source);
        _jobs.Add(job);
        Touch();

        Raise(new Events.ConversionJobAdded(Id, job.Id, source.Location));

        return job;
    }

    /// <summary>Removes a job.</summary>
    /// <param name="jobId">The job to remove.</param>
    /// <returns><see langword="true"/> when the job was present and removed.</returns>
    /// <exception cref="InvalidConversionStateException">The job is currently running.</exception>
    public bool RemoveJob(ConversionJobId jobId)
    {
        ConversionJob? job = _jobs.Find(candidate => candidate.Id == jobId);

        if (job is null)
        {
            return false;
        }

        if (job.Status == Enums.ConversionStatus.Running)
        {
            throw InvalidConversionStateException.For(job.Status, "remove the job");
        }

        _jobs.Remove(job);
        Touch();

        return true;
    }

    /// <summary>Queues every job that is not already running or finished.</summary>
    /// <returns>The jobs that were queued.</returns>
    /// <exception cref="Exceptions.DomainValidationException">The project has no jobs.</exception>
    public IReadOnlyList<ConversionJob> QueueAll()
    {
        Validate().ThrowIfInvalid();

        List<ConversionJob> queued = [];

        foreach (ConversionJob job in _jobs.Where(static job =>
            job.Status is Enums.ConversionStatus.Draft
                or Enums.ConversionStatus.Failed
                or Enums.ConversionStatus.Cancelled))
        {
            job.Queue();
            queued.Add(job);
            Raise(new Events.ConversionJobQueued(Id, job.Id));
        }

        Touch();

        return queued;
    }

    /// <summary>Renames the project.</summary>
    /// <param name="name">The new name.</param>
    public void Rename(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Name = name.Trim();
        Touch();
    }

    /// <summary>Replaces the description.</summary>
    /// <param name="description">The new description, or null to clear it.</param>
    public void Describe(string? description)
    {
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        Touch();
    }

    /// <summary>Replaces the conversion settings.</summary>
    /// <param name="settings">The new settings.</param>
    /// <exception cref="InvalidConversionStateException">A job is currently running.</exception>
    public void UpdateSettings(ConversionSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        if (_jobs.Any(static job => job.Status == Enums.ConversionStatus.Running))
        {
            throw new InvalidConversionStateException(
                "Settings cannot be changed while a job in this project is running.");
        }

        Settings = settings;
        Touch();
    }

    /// <inheritdoc />
    public ValidationOutcome Validate() =>
        new ValidationOutcome()
            .AddIf(_jobs.Count == 0, nameof(Jobs), "A project must contain at least one job.", "Project.NoJobs")
            .AddIf(
                string.IsNullOrWhiteSpace(Name),
                nameof(Name),
                "A project must have a name.",
                "Project.NameRequired");

    private void Touch() => ModifiedAtUtc = DateTimeOffset.UtcNow;
}
