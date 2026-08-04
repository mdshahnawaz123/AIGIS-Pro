using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.Entities.QaQc;

/// <summary>
/// The findings from validating one conversion run, with the summary the operator actually reads.
/// </summary>
/// <remarks>
/// Built once and then immutable. Counts are computed at construction rather than on each access,
/// because a report over a large dataset is rendered repeatedly &#8212; on screen, to HTML, to CSV
/// &#8212; and recomputing a group-by each time would be wasteful for a value that cannot change.
/// </remarks>
public sealed class ValidationReport
{
    private readonly Dictionary<IssueSeverity, int> _countsBySeverity;
    private readonly Dictionary<IssueCategory, int> _countsByCategory;

    /// <summary>Initializes a new instance of the <see cref="ValidationReport"/> class.</summary>
    /// <param name="runId">The run these findings concern.</param>
    /// <param name="issues">The findings.</param>
    public ValidationReport(ConversionRunId runId, IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        RunId = runId;
        Issues = [.. issues];
        GeneratedAtUtc = DateTimeOffset.UtcNow;

        _countsBySeverity = Issues
            .GroupBy(static issue => issue.Severity)
            .ToDictionary(static group => group.Key, static group => group.Count());

        _countsByCategory = Issues
            .GroupBy(static issue => issue.Category)
            .ToDictionary(static group => group.Key, static group => group.Count());

        HighestSeverity = Issues.Count == 0
            ? IssueSeverity.Information
            : Issues.Max(static issue => issue.Severity);
    }

    /// <summary>Gets the run these findings concern.</summary>
    public ConversionRunId RunId { get; }

    /// <summary>Gets the findings.</summary>
    public IReadOnlyList<ValidationIssue> Issues { get; }

    /// <summary>Gets the instant the report was produced.</summary>
    public DateTimeOffset GeneratedAtUtc { get; }

    /// <summary>Gets the most serious finding, or <see cref="IssueSeverity.Information"/> when there are none.</summary>
    public IssueSeverity HighestSeverity { get; }

    /// <summary>Gets the total number of findings.</summary>
    public int TotalCount => Issues.Count;

    /// <summary>Gets a value indicating whether any finding is critical.</summary>
    public bool HasCriticalIssues => CountOf(IssueSeverity.Critical) > 0;

    /// <summary>Counts findings of a severity.</summary>
    /// <param name="severity">The severity to count.</param>
    /// <returns>The number of findings.</returns>
    public int CountOf(IssueSeverity severity) => _countsBySeverity.GetValueOrDefault(severity);

    /// <summary>Counts findings in a category.</summary>
    /// <param name="category">The category to count.</param>
    /// <returns>The number of findings.</returns>
    public int CountOf(IssueCategory category) => _countsByCategory.GetValueOrDefault(category);

    /// <summary>Gets the findings at or above a severity, most serious first.</summary>
    /// <param name="minimumSeverity">The lowest severity to include.</param>
    /// <returns>The matching findings.</returns>
    public IReadOnlyList<ValidationIssue> AtOrAbove(IssueSeverity minimumSeverity) =>
        Issues
            .Where(issue => issue.Severity >= minimumSeverity)
            .OrderByDescending(static issue => issue.Severity)
            .ThenBy(static issue => issue.Code, StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Determines whether the run may proceed to export under a given tolerance.
    /// </summary>
    /// <param name="failAtOrAbove">The severity at which the run should be stopped.</param>
    /// <returns><see langword="true"/> when no finding reaches the stated severity.</returns>
    public bool IsAcceptable(IssueSeverity failAtOrAbove) => HighestSeverity < failAtOrAbove;

    /// <inheritdoc />
    public override string ToString() =>
        $"ValidationReport({TotalCount} issues, highest {HighestSeverity})";
}
