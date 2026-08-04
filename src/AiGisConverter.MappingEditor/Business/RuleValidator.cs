using System;
using System.Collections.Generic;
using System.Linq;
using AiGisConverter.Business.Classification;

namespace AiGisConverter.MappingEditor.Business;

public enum ValidationSeverity
{
    Info,
    Warning,
    Error
}

public class ValidationIssue
{
    public string RuleName { get; set; } = string.Empty;
    public ValidationSeverity Severity { get; set; }
    public string Message { get; set; } = string.Empty;
}

public class RuleValidator
{
    public IReadOnlyList<ValidationIssue> ValidateProfile(MappingProfile profile)
    {
        var issues = new List<ValidationIssue>();

        if (profile == null)
        {
            return issues;
        }

        var ruleNames = new HashSet<string>();

        foreach (var rule in profile.Rules)
        {
            // Duplicate Rule Name
            if (!ruleNames.Add(rule.RuleName))
            {
                issues.Add(new ValidationIssue
                {
                    RuleName = rule.RuleName,
                    Severity = ValidationSeverity.Error,
                    Message = $"Duplicate rule name '{rule.RuleName}' found."
                });
            }

            // Missing Feature Class
            if (string.IsNullOrWhiteSpace(rule.TargetFeatureClass))
            {
                issues.Add(new ValidationIssue
                {
                    RuleName = rule.RuleName,
                    Severity = ValidationSeverity.Error,
                    Message = "Target Feature Class is required."
                });
            }

            // Missing Layer Names (Not strictly required, but usually an error if empty and no other conditions exist)
            bool hasCondition = (rule.LayerNames != null && rule.LayerNames.Length > 0) ||
                                (rule.EntityTypes != null && rule.EntityTypes.Length > 0) ||
                                (rule.BlockNames != null && rule.BlockNames.Length > 0) ||
                                (rule.TextPattern != null) ||
                                (rule.GeometryTypes != null && rule.GeometryTypes.Length > 0);

            if (!hasCondition)
            {
                issues.Add(new ValidationIssue
                {
                    RuleName = rule.RuleName,
                    Severity = ValidationSeverity.Warning,
                    Message = "Rule has no matching conditions and will never match (or will match everything)."
                });
            }

            // Impossible Conditions
            if (rule.MinimumArea.HasValue && rule.MaximumArea.HasValue && rule.MinimumArea > rule.MaximumArea)
            {
                issues.Add(new ValidationIssue
                {
                    RuleName = rule.RuleName,
                    Severity = ValidationSeverity.Error,
                    Message = "Minimum Area cannot be greater than Maximum Area."
                });
            }

            if (rule.MinimumLength.HasValue && rule.MaximumLength.HasValue && rule.MinimumLength > rule.MaximumLength)
            {
                issues.Add(new ValidationIssue
                {
                    RuleName = rule.RuleName,
                    Severity = ValidationSeverity.Error,
                    Message = "Minimum Length cannot be greater than Maximum Length."
                });
            }
        }

        // Detect Conflicting Rules (Same conditions, different priority/feature class)
        for (int i = 0; i < profile.Rules.Count; i++)
        {
            for (int j = i + 1; j < profile.Rules.Count; j++)
            {
                var r1 = profile.Rules[i];
                var r2 = profile.Rules[j];

                if (AreConditionsIdentical(r1, r2))
                {
                    if (r1.Priority == r2.Priority)
                    {
                        issues.Add(new ValidationIssue
                        {
                            RuleName = r1.RuleName,
                            Severity = ValidationSeverity.Warning,
                            Message = $"Conflicts with '{r2.RuleName}' due to identical conditions and same priority."
                        });
                    }
                    else
                    {
                        var lowerPriority = r1.Priority < r2.Priority ? r1.RuleName : r2.RuleName;
                        var higherPriority = r1.Priority > r2.Priority ? r1.RuleName : r2.RuleName;
                        issues.Add(new ValidationIssue
                        {
                            RuleName = lowerPriority,
                            Severity = ValidationSeverity.Info,
                            Message = $"Shadowed by '{higherPriority}' which has identical conditions but higher priority."
                        });
                    }
                }
            }
        }

        return issues;
    }

    private bool AreConditionsIdentical(MappingRule r1, MappingRule r2)
    {
        return ArraysEqual(r1.LayerNames, r2.LayerNames) &&
               ArraysEqual(r1.EntityTypes, r2.EntityTypes) &&
               ArraysEqual(r1.GeometryTypes, r2.GeometryTypes) &&
               ArraysEqual(r1.BlockNames, r2.BlockNames) &&
               r1.TextPattern == r2.TextPattern &&
               r1.MinimumArea == r2.MinimumArea &&
               r1.MaximumArea == r2.MaximumArea;
    }

    private bool ArraysEqual(string[]? a, string[]? b)
    {
        if (a == null && b == null)
        {
            return true;
        }

        if (a == null || b == null)
        {
            return false;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        var setA = new HashSet<string>(a);
        return setA.SetEquals(b);
    }
}
