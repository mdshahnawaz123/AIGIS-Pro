using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.Abstractions.Repositories;

/// <summary>
/// Persistence for validation reports.
/// </summary>
/// <remarks>
/// A report is written once and read many times, and it is always reached through its run, so it
/// has no update or delete surface of its own. Removing a report happens by pruning its run.
/// </remarks>
public interface IValidationReportRepository
{
    /// <summary>Stores a report.</summary>
    /// <param name="report">The report to store.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>A task that completes when the report has been staged.</returns>
    Task AddAsync(ValidationReport report, CancellationToken cancellationToken = default);

    /// <summary>Loads the report for a run.</summary>
    /// <param name="runId">The run.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The report, or <see langword="null"/> when the run was not validated.</returns>
    Task<ValidationReport?> GetForRunAsync(
        ConversionRunId runId,
        CancellationToken cancellationToken = default);

    /// <summary>Counts findings of a severity across a project's history.</summary>
    /// <param name="projectId">The project.</param>
    /// <param name="severity">The severity to count.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of findings.</returns>
    Task<int> CountBySeverityAsync(
        ProjectId projectId,
        IssueSeverity severity,
        CancellationToken cancellationToken = default);
}
