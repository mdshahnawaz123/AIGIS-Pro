using System.Diagnostics;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Abstractions;
using AiGisConverter.QaQc.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.QaQc.Engine;

/// <summary>
/// Default <see cref="IQaQcEngine"/>: runs every registered rule over every dataset.
/// </summary>
/// <remarks>
/// <para>
/// A rule that throws is contained. Its failure becomes a finding of its own and the remaining
/// rules still run, because a report naming twelve real defects and one broken rule is more useful
/// than no report at all.
/// </para>
/// <para>
/// Findings are capped per rule. A dataset with a systematic fault raises one finding per feature,
/// and ten thousand identical lines bury everything else. The cap is recorded in the report so the
/// truncation is visible rather than silent.
/// </para>
/// </remarks>
public sealed class QaQcEngine : IQaQcEngine
{
    private readonly IEnumerable<IValidationRuleSource> _ruleSources;
    private readonly IOptionsMonitor<QaQcOptions> _options;
    private readonly ILogger<QaQcEngine> _logger;

    /// <summary>Initializes a new instance of the <see cref="QaQcEngine"/> class.</summary>
    /// <param name="ruleSources">Everything that can supply rules, built in or contributed by a plugin.</param>
    /// <param name="options">Live QA/QC settings.</param>
    /// <param name="logger">Logger for the engine.</param>
    public QaQcEngine(
        IEnumerable<IValidationRuleSource> ruleSources,
        IOptionsMonitor<QaQcOptions> options,
        ILogger<QaQcEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(ruleSources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _ruleSources = ruleSources;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public Task<Result<ValidationReport>> ValidateAsync(
        ConversionRunId runId,
        IReadOnlyList<GisDataset> datasets,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(datasets);

        // Rules are CPU-bound and synchronous. Wrapping keeps a caller's UI thread free without
        // pretending the work is asynchronous.
        return Task.Run(() => Validate(runId, datasets, progress, cancellationToken), cancellationToken);
    }

    private Result<ValidationReport> Validate(
        ConversionRunId runId,
        IReadOnlyList<GisDataset> datasets,
        IProgress<string>? progress,
        CancellationToken cancellationToken)
    {
        QaQcOptions options = _options.CurrentValue;
        IReadOnlyList<IValidationRule> rules = ResolveRules(options);

        if (rules.Count == 0)
        {
            return Result.Failure<ValidationReport>(new Error(
                "QaQc.NoRules",
                "No validation rules are registered, so nothing could be checked."));
        }

        long startedAt = Stopwatch.GetTimestamp();
        List<ValidationIssue> findings = [];

        foreach (GisDataset dataset in datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();

            progress?.Report($"Validating {dataset.FeatureClass.Name}...");

            ValidationContext context = new(dataset, options);

            if (!context.AllowsWholeDatasetRules)
            {
                findings.Add(ValidationIssue.Create(
                    IssueSeverity.Information,
                    IssueCategory.Topology,
                    "QaQc.TopologySkipped",
                    $"'{dataset.FeatureClass.Name}' has {dataset.Features.Count:N0} features, above the " +
                    $"cross-feature ceiling of {options.TopologyFeatureCeiling:N0}. Topology rules were skipped.")
                    .WithRemediation("Raise 'QaQc:TopologyFeatureCeiling' to check it anyway."));
            }

            foreach (IValidationRule rule in rules)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (rule.RequiresWholeDataset && !context.AllowsWholeDatasetRules)
                {
                    continue;
                }

                findings.AddRange(RunRule(rule, context, options, cancellationToken));
            }
        }

        ValidationReport report = new(runId, findings);

        _logger.LogInformation(
            "Validated {DatasetCount} datasets with {RuleCount} rules in {ElapsedMs} ms: " +
            "{IssueCount} findings, highest severity {Severity}.",
            datasets.Count,
            rules.Count,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds,
            report.TotalCount,
            report.HighestSeverity);

        return Result.Success(report);
    }

    /// <summary>Runs one rule, containing its failure and enforcing the finding cap.</summary>
    private List<ValidationIssue> RunRule(
        IValidationRule rule,
        ValidationContext context,
        QaQcOptions options,
        CancellationToken cancellationToken)
    {
        List<ValidationIssue> findings = [];

        try
        {
            foreach (ValidationIssue issue in rule.Validate(context, cancellationToken))
            {
                if (findings.Count >= options.MaximumFindingsPerRule)
                {
                    findings.Add(ValidationIssue.Create(
                        IssueSeverity.Information,
                        rule.Category,
                        "QaQc.FindingsTruncated",
                        $"Rule '{rule.RuleId}' reached the limit of {options.MaximumFindingsPerRule:N0} " +
                        $"findings on '{context.Dataset.FeatureClass.Name}'. Further findings were suppressed.")
                        .WithRemediation(
                            "A rule hitting its cap usually means a systematic fault rather than " +
                            "many separate ones. Fix the cause and re-run."));

                    break;
                }

                findings.Add(issue);
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is InvalidOperationException
                                       or ArgumentException
                                       or NotSupportedException
                                       or NetTopologySuite.Geometries.TopologyException)
        {
            // A broken rule must not cost the findings the other rules produced.
            _logger.LogError(ex, "Rule {RuleId} failed on {Dataset}.", rule.RuleId, context.Dataset.FeatureClass.Name);

            findings.Add(ValidationIssue.Create(
                IssueSeverity.Error,
                rule.Category,
                "QaQc.RuleFailed",
                $"Rule '{rule.RuleId}' failed on '{context.Dataset.FeatureClass.Name}': {ex.Message}. " +
                "Its findings, if any, are missing from this report."));
        }

        return findings;
    }

    /// <summary>Collects rules from every source, dropping duplicates and anything disabled.</summary>
    private IReadOnlyList<IValidationRule> ResolveRules(QaQcOptions options)
    {
        Dictionary<string, IValidationRule> byId = new(StringComparer.OrdinalIgnoreCase);

        foreach (IValidationRuleSource source in _ruleSources)
        {
            foreach (IValidationRule rule in source.GetRules())
            {
                if (options.DisabledRules.Contains(rule.RuleId, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!byId.TryAdd(rule.RuleId, rule))
                {
                    // A plugin colliding with a built-in id is ignored rather than fatal: one badly
                    // named third-party rule must not disable quality checking altogether.
                    _logger.LogWarning(
                        "Two validation rules declare the id '{RuleId}'. The first was kept.",
                        rule.RuleId);
                }
            }
        }

        return [.. byId.Values.OrderBy(static r => r.RuleId, StringComparer.Ordinal)];
    }
}

/// <summary>Supplies the rules compiled into this assembly.</summary>
public sealed class BuiltInValidationRuleSource : IValidationRuleSource
{
    private readonly IEnumerable<IValidationRule> _rules;

    /// <summary>Initializes a new instance of the <see cref="BuiltInValidationRuleSource"/> class.</summary>
    /// <param name="rules">The rules registered with the container.</param>
    public BuiltInValidationRuleSource(IEnumerable<IValidationRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _rules = rules;
    }

    /// <inheritdoc />
    public IEnumerable<IValidationRule> GetRules() => _rules;
}
