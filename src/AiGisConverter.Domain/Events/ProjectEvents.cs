using AiGisConverter.Domain.Common;

namespace AiGisConverter.Domain.Events;

/// <summary>Raised when a conversion project is created.</summary>
/// <param name="ProjectId">The new project.</param>
/// <param name="Name">The project name.</param>
public sealed record ConversionProjectCreated(ProjectId ProjectId, string Name) : DomainEvent;

/// <summary>Raised when a job is added to a project.</summary>
/// <param name="ProjectId">The owning project.</param>
/// <param name="JobId">The new job.</param>
/// <param name="SourceLocation">The source the job will read.</param>
public sealed record ConversionJobAdded(
    ProjectId ProjectId,
    ConversionJobId JobId,
    string SourceLocation) : DomainEvent;

/// <summary>Raised when a job is queued for execution.</summary>
/// <param name="ProjectId">The owning project.</param>
/// <param name="JobId">The queued job.</param>
public sealed record ConversionJobQueued(ProjectId ProjectId, ConversionJobId JobId) : DomainEvent;
