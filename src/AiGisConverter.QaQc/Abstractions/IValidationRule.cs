using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.QaQc.Abstractions;

/// <summary>
/// One data-quality rule.
/// </summary>
/// <remarks>
/// <para>
/// A plugin-contributable capability. A site with its own submission checks registers an
/// <see cref="IValidationRule"/> from a plugin and the engine picks it up, exactly as it picks up
/// an AI provider &#8212; no change to this assembly and no recompilation of the host.
/// </para>
/// <para>
/// A rule reports; it never repairs and never decides the outcome of a run. Whether an
/// <see cref="IssueSeverity.Error"/> stops a delivery is a policy question answered by
/// configuration, not by the rule that found it.
/// </para>
/// </remarks>
public interface IValidationRule
{
    /// <summary>Gets the stable rule identifier, for example <c>Topology.Overlaps</c>.</summary>
    /// <remarks>Used to disable the rule in configuration, so it must not change between releases.</remarks>
    string RuleId { get; }

    /// <summary>Gets the human-readable rule name shown in the report.</summary>
    string DisplayName { get; }

    /// <summary>Gets the category the findings belong to.</summary>
    IssueCategory Category { get; }

    /// <summary>
    /// Gets a value indicating whether the rule needs every feature of a dataset at once.
    /// </summary>
    /// <remarks>
    /// Cross-feature rules are subject to the configured feature ceiling, because their cost grows
    /// with the square of the dataset in the worst case. Per-feature rules are not.
    /// </remarks>
    bool RequiresWholeDataset { get; }

    /// <summary>Runs the rule.</summary>
    /// <param name="context">The dataset under inspection and the thresholds in force.</param>
    /// <param name="cancellationToken">Token used to cancel the rule.</param>
    /// <returns>The findings. Empty when the rule is satisfied.</returns>
    IEnumerable<ValidationIssue> Validate(ValidationContext context, CancellationToken cancellationToken = default);
}

/// <summary>Supplies validation rules to the engine.</summary>
/// <remarks>
/// Indirected for the same reason as the AI provider source: plugin-contributed rules do not exist
/// when the container is built, so the engine must be able to ask what rules there are <em>now</em>
/// rather than being handed a list that was correct a moment earlier.
/// </remarks>
public interface IValidationRuleSource
{
    /// <summary>Gets the rules this source currently offers.</summary>
    /// <returns>The available rules. May differ between calls.</returns>
    IEnumerable<IValidationRule> GetRules();
}
