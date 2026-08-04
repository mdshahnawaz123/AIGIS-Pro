using AiGisConverter.Data.Context;
using AiGisConverter.Domain.Abstractions.Repositories;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AiGisConverter.Data.Repositories;

/// <summary>
/// Persistence for validation reports.
/// </summary>
/// <remarks>
/// The report is stored as its findings and recomposed on read. It is a computed view over them,
/// and materialising its counts as columns would create a summary that can drift from the findings
/// it claims to summarise.
/// </remarks>
public sealed class ValidationReportRepository : IValidationReportRepository
{
    private readonly AiGisConverterDbContext _context;

    /// <summary>Initializes a new instance of the <see cref="ValidationReportRepository"/> class.</summary>
    /// <param name="context">The database context.</param>
    public ValidationReportRepository(AiGisConverterDbContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <inheritdoc />
    public async Task AddAsync(ValidationReport report, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);

        foreach (ValidationIssue issue in report.Issues)
        {
            Microsoft.EntityFrameworkCore.ChangeTracking.EntityEntry<ValidationIssue> entry =
                await _context.ValidationIssues.AddAsync(issue, cancellationToken).ConfigureAwait(false);

            entry.Property<Guid>("RunId").CurrentValue = report.RunId.Value;
        }
    }

    /// <inheritdoc />
    public async Task<ValidationReport?> GetForRunAsync(
        ConversionRunId runId,
        CancellationToken cancellationToken = default)
    {
        List<ValidationIssue> issues = await _context.ValidationIssues
            .AsNoTracking()
            .Where(issue => EF.Property<Guid>(issue, "RunId") == runId.Value)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        // No findings and no run are indistinguishable here, so absence is reported rather than an
        // empty report invented. A clean run still writes its findings list, which is empty but
        // present, and the caller can tell the two apart by asking the run repository.
        return issues.Count == 0 ? null : new ValidationReport(runId, issues);
    }

    /// <inheritdoc />
    public Task<int> CountBySeverityAsync(
        ProjectId projectId,
        IssueSeverity severity,
        CancellationToken cancellationToken = default) =>
        _context.ValidationIssues
            .AsNoTracking()
            .Where(issue => issue.Severity == severity)
            .Join(
                _context.Runs.Where(run => run.ProjectId == projectId),
                issue => EF.Property<Guid>(issue, "RunId"),
                run => run.Id.Value,
                (issue, run) => issue)
            .CountAsync(cancellationToken);
}
