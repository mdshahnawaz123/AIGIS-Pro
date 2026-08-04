using System.Linq.Expressions;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.Specifications;

/// <summary>Selects runs in a given status.</summary>
public sealed class RunsWithStatusSpecification : Specification<ConversionRun>
{
    private readonly ConversionStatus _status;

    /// <summary>Initializes a new instance of the <see cref="RunsWithStatusSpecification"/> class.</summary>
    /// <param name="status">The status to match.</param>
    public RunsWithStatusSpecification(ConversionStatus status) => _status = status;

    /// <inheritdoc />
    public override Expression<Func<ConversionRun, bool>> ToExpression() => run => run.Status == _status;
}

/// <summary>Selects runs that finished within a window.</summary>
public sealed class RunsFinishedBetweenSpecification : Specification<ConversionRun>
{
    private readonly DateTimeOffset _fromInclusive;
    private readonly DateTimeOffset _toExclusive;

    /// <summary>Initializes a new instance of the <see cref="RunsFinishedBetweenSpecification"/> class.</summary>
    /// <param name="fromInclusive">Start of the window, inclusive.</param>
    /// <param name="toExclusive">End of the window, exclusive.</param>
    public RunsFinishedBetweenSpecification(DateTimeOffset fromInclusive, DateTimeOffset toExclusive)
    {
        _fromInclusive = fromInclusive;
        _toExclusive = toExclusive;
    }

    /// <inheritdoc />
    public override Expression<Func<ConversionRun, bool>> ToExpression() =>
        run => run.FinishedAtUtc != null
               && run.FinishedAtUtc >= _fromInclusive
               && run.FinishedAtUtc < _toExclusive;
}

/// <summary>Selects runs belonging to a project.</summary>
public sealed class RunsForProjectSpecification : Specification<ConversionRun>
{
    private readonly ProjectId _projectId;

    /// <summary>Initializes a new instance of the <see cref="RunsForProjectSpecification"/> class.</summary>
    /// <param name="projectId">The project to match.</param>
    public RunsForProjectSpecification(ProjectId projectId) => _projectId = projectId;

    /// <inheritdoc />
    public override Expression<Func<ConversionRun, bool>> ToExpression() => run => run.ProjectId == _projectId;
}

/// <summary>
/// Selects runs an operator should look at: failed, or succeeded with findings.
/// </summary>
/// <remarks>
/// Expressed once, here, rather than as an ad-hoc filter at each call site, so that the definition
/// of "needs attention" cannot drift between the dashboard, the batch summary and the report.
/// </remarks>
public sealed class RunsNeedingAttentionSpecification : Specification<ConversionRun>
{
    /// <inheritdoc />
    public override Expression<Func<ConversionRun, bool>> ToExpression() =>
        run => run.Status == ConversionStatus.Failed
               || run.Status == ConversionStatus.SucceededWithWarnings
               || run.HighestSeverity >= IssueSeverity.Error;
}
