using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.ValueObjects;
using System.Collections.Generic;
using System.Linq;
using System;
using System.Text.RegularExpressions;

using AiGisConverter.Domain.Abstractions.Services;

namespace AiGisConverter.Business.Classification;

/// <summary>
/// Evaluates mapping profiles to determine the classification of a source element.
/// </summary>
public sealed class ClassificationEngine : IRuleEngine
{
    private readonly List<MappingProfile> _profiles = new();

    /// <summary>
    /// Adds a profile to the engine.
    /// </summary>
    /// <param name="profile">The profile to add.</param>
    public void AddProfile(MappingProfile profile)
    {
        _profiles.Add(profile);
    }

    /// <summary>
    /// Evaluates the rules and returns all matching candidates sorted by priority and confidence.
    /// </summary>
    public IReadOnlyList<ClassificationCandidate> Evaluate(SourceElement element)
    {
        var candidates = new List<ClassificationCandidate>();

        foreach (var profile in _profiles)
        {
            foreach (var rule in profile.Rules)
            {
                if (IsMatch(rule, element, out double matchScore))
                {
                    candidates.Add(new ClassificationCandidate(
                        rule.TargetFeatureClass,
                        Confidence.FromScore(matchScore),
                        rule.RuleName,
                        rule.Priority,
                        $"Matched rule '{rule.RuleName}' in profile '{profile.Name}'."
                    ));
                }
            }
        }

        return candidates
            .OrderByDescending(c => c.Priority)
            .ThenByDescending(c => c.Confidence.Value)
            .ToList();
    }

    /// <summary>
    /// Evaluates the rules against a semantic feature.
    /// </summary>
    public IReadOnlyList<ClassificationCandidate> Evaluate(AiGisConverter.Domain.Entities.Semantic.SemanticFeature feature)
    {
        var candidates = new List<ClassificationCandidate>();

        foreach (var profile in _profiles)
        {
            foreach (var rule in profile.Rules)
            {
                if (IsMatch(rule, feature, out double matchScore))
                {
                    candidates.Add(new ClassificationCandidate(
                        rule.TargetFeatureClass,
                        Confidence.FromScore(matchScore),
                        rule.RuleName,
                        rule.Priority,
                        $"Matched semantic rule '{rule.RuleName}' in profile '{profile.Name}'."
                    ));
                }
            }
        }

        return candidates
            .OrderByDescending(c => c.Priority)
            .ThenByDescending(c => c.Confidence.Value)
            .ToList();
    }

    private static bool IsMatch(MappingRule rule, SourceElement element, out double score)
    {
        score = 0;
        int conditionsChecked = 0;

        if (rule.LayerNames?.Length > 0)
        {
            conditionsChecked++;
            if (!element.Attributes.TryGetValue("Layer", out var layer) || !rule.LayerNames.Contains(layer?.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.EntityTypes?.Length > 0)
        {
            conditionsChecked++;
            if (element.NativeType == null || !rule.EntityTypes.Contains(element.NativeType, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.GeometryTypes?.Length > 0)
        {
            conditionsChecked++;
            if (!rule.GeometryTypes.Contains(element.GeometryKind.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.BlockNames?.Length > 0)
        {
            conditionsChecked++;
            if (!element.Attributes.TryGetValue("BlockName", out var block) || !rule.BlockNames.Contains(block?.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.Colors?.Length > 0)
        {
            conditionsChecked++;
            if (!element.Attributes.TryGetValue("Color", out var color) || !rule.Colors.Contains(color?.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.LineTypes?.Length > 0)
        {
            conditionsChecked++;
            if (!element.Attributes.TryGetValue("Linetype", out var linetype) || !rule.LineTypes.Contains(linetype?.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        
        if (rule.XData?.Length > 0)
        {
            conditionsChecked++;
            if (!element.Attributes.TryGetValue("XData", out var xdata) || xdata == null)
            {
                return false;
            }
            
            var xdataStr = xdata.ToString() ?? "";
            if (!rule.XData.Any(x => xdataStr.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.TextPattern))
        {
            conditionsChecked++;
            if (string.IsNullOrWhiteSpace(element.Text) || !Regex.IsMatch(element.Text, rule.TextPattern, RegexOptions.IgnoreCase))
            {
                return false;
            }
        }

        if (rule.MinimumArea.HasValue || rule.MaximumArea.HasValue)
        {
            conditionsChecked++;
            if (!element.Attributes.TryGetValue("Area", out var areaObj) || areaObj is not double area)
            {
                return false;
            }

            if (rule.MinimumArea.HasValue && area < rule.MinimumArea.Value)
            {
                return false;
            }
            
            if (rule.MaximumArea.HasValue && area > rule.MaximumArea.Value)
            {
                return false;
            }
        }

        if (rule.MinimumLength.HasValue || rule.MaximumLength.HasValue)
        {
            conditionsChecked++;
            if (!element.Attributes.TryGetValue("Length", out var lenObj) || lenObj is not double length)
            {
                return false;
            }

            if (rule.MinimumLength.HasValue && length < rule.MinimumLength.Value)
            {
                return false;
            }
            
            if (rule.MaximumLength.HasValue && length > rule.MaximumLength.Value)
            {
                return false;
            }
        }

        if (rule.Attributes != null && rule.Attributes.Count > 0)
        {
            conditionsChecked += rule.Attributes.Count;
            foreach (var kvp in rule.Attributes)
            {
                if (!element.Attributes.TryGetValue(kvp.Key, out var val) || val?.ToString() != kvp.Value)
                {
                    return false;
                }
            }
        }

        if (rule.Conditions != null && rule.Conditions.Count > 0)
        {
            conditionsChecked += rule.Conditions.Count;
            foreach (var condition in rule.Conditions)
            {
                if (!element.Attributes.TryGetValue(condition.Key, out var value) || value == null)
                {
                    return false;
                }
                
                if (!string.Equals(value.ToString(), condition.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (conditionsChecked == 0)
        {
            return false;
        }

        // Calculate a basic confidence score: more specific rules (more conditions checked) yield higher confidence.
        // E.g., 1 condition = 0.85, 2 conditions = 0.90, 3+ conditions = 0.98.
        score = conditionsChecked switch
        {
            1 => 0.85,
            2 => 0.90,
            _ => 0.98
        };

        return true;
    }
    private static bool IsMatch(MappingRule rule, AiGisConverter.Domain.Entities.Semantic.SemanticFeature feature, out double score)
    {
        score = 0;
        int conditionsChecked = 0;

        if (rule.LayerNames?.Length > 0)
        {
            conditionsChecked++;
            if (feature.Layer == null || !rule.LayerNames.Contains(feature.Layer, StringComparer.OrdinalIgnoreCase))
            {
                // Fallback to RawSource Layer
                if (!feature.RawSource.Attributes.TryGetValue("Layer", out var rawLayer) || !rule.LayerNames.Contains(rawLayer?.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (rule.EntityTypes?.Length > 0)
        {
            conditionsChecked++;
            // Check Category/Family first, fallback to native type
            bool matched = false;
            if (feature.Category != null && rule.EntityTypes.Contains(feature.Category, StringComparer.OrdinalIgnoreCase)) 
            {
                matched = true;
            }
            else if (feature.Family != null && rule.EntityTypes.Contains(feature.Family, StringComparer.OrdinalIgnoreCase)) 
            {
                matched = true;
            }
            else if (feature.RawSource.NativeType != null && rule.EntityTypes.Contains(feature.RawSource.NativeType, StringComparer.OrdinalIgnoreCase)) 
            {
                matched = true;
            }

            if (!matched)
            {
                return false;
            }
        }

        if (rule.GeometryTypes?.Length > 0)
        {
            conditionsChecked++;
            if (!rule.GeometryTypes.Contains(feature.GeometryKind.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        if (rule.BlockNames?.Length > 0)
        {
            conditionsChecked++;
            if (feature.Block == null || !rule.BlockNames.Contains(feature.Block, StringComparer.OrdinalIgnoreCase))
            {
                if (!feature.RawSource.Attributes.TryGetValue("BlockName", out var block) || !rule.BlockNames.Contains(block?.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (rule.Colors?.Length > 0)
        {
            conditionsChecked++;
            if (feature.Color == null || !rule.Colors.Contains(feature.Color, StringComparer.OrdinalIgnoreCase))
            {
                if (!feature.RawSource.Attributes.TryGetValue("Color", out var color) || !rule.Colors.Contains(color?.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (rule.LineTypes?.Length > 0)
        {
            conditionsChecked++;
            if (feature.Linetype == null || !rule.LineTypes.Contains(feature.Linetype, StringComparer.OrdinalIgnoreCase))
            {
                if (!feature.RawSource.Attributes.TryGetValue("Linetype", out var linetype) || !rule.LineTypes.Contains(linetype?.ToString(), StringComparer.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }
        
        if (rule.XData?.Length > 0)
        {
            conditionsChecked++;
            if (!feature.RawSource.Attributes.TryGetValue("XData", out var xdata) || xdata == null)
            {
                return false;
            }
            
            var xdataStr = xdata.ToString() ?? "";
            if (!rule.XData.Any(x => xdataStr.Contains(x, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(rule.TextPattern))
        {
            conditionsChecked++;
            if (string.IsNullOrWhiteSpace(feature.RawSource.Text) || !Regex.IsMatch(feature.RawSource.Text, rule.TextPattern, RegexOptions.IgnoreCase))
            {
                return false;
            }
        }

        if (rule.MinimumArea.HasValue || rule.MaximumArea.HasValue)
        {
            conditionsChecked++;
            double area = feature.Area ?? (feature.RawSource.Attributes.TryGetValue("Area", out var areaObj) && areaObj is double a ? a : 0);
            if (area == 0)
            {
                return false;
            }

            if (rule.MinimumArea.HasValue && area < rule.MinimumArea.Value)
            {
                return false;
            }
            if (rule.MaximumArea.HasValue && area > rule.MaximumArea.Value)
            {
                return false;
            }
        }

        if (rule.MinimumLength.HasValue || rule.MaximumLength.HasValue)
        {
            conditionsChecked++;
            double length = feature.Length ?? (feature.RawSource.Attributes.TryGetValue("Length", out var lenObj) && lenObj is double l ? l : 0);
            if (length == 0)
            {
                return false;
            }

            if (rule.MinimumLength.HasValue && length < rule.MinimumLength.Value)
            {
                return false;
            }
            if (rule.MaximumLength.HasValue && length > rule.MaximumLength.Value)
            {
                return false;
            }
        }

        if (rule.Attributes != null && rule.Attributes.Count > 0)
        {
            conditionsChecked += rule.Attributes.Count;
            foreach (var kvp in rule.Attributes)
            {
                if (!feature.RawSource.Attributes.TryGetValue(kvp.Key, out var val) || val?.ToString() != kvp.Value)
                {
                    return false;
                }
            }
        }

        if (rule.Conditions != null && rule.Conditions.Count > 0)
        {
            conditionsChecked += rule.Conditions.Count;
            foreach (var condition in rule.Conditions)
            {
                if (!feature.RawSource.Attributes.TryGetValue(condition.Key, out var value) || value == null)
                {
                    return false;
                }
                
                if (!string.Equals(value.ToString(), condition.Value, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }
        }

        if (conditionsChecked == 0)
        {
            return false;
        }

        // Semantic matches get slightly higher baseline confidence
        score = conditionsChecked switch
        {
            1 => 0.88,
            2 => 0.93,
            _ => 0.99
        };

        return true;
    }
}
