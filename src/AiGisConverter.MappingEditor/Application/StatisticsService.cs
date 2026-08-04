using System.Collections.Generic;
using System.Linq;
using AiGisConverter.MappingEditor.Business;

namespace AiGisConverter.MappingEditor.Application;

public class ClassificationStatistics
{
    public int TotalFeatures { get; set; }
    public int UnclassifiedFeatures { get; set; }
    public double AverageConfidence { get; set; }
    public Dictionary<string, int> FeatureClassCounts { get; set; } = new();
    public Dictionary<string, int> RuleUsageCounts { get; set; } = new();
}

public class StatisticsService
{
    public ClassificationStatistics CalculateStatistics(IReadOnlyList<SimulationResult> results)
    {
        var stats = new ClassificationStatistics
        {
            TotalFeatures = results.Count,
            UnclassifiedFeatures = results.Count(r => r.MatchedRule == null),
            AverageConfidence = results.Any(r => r.MatchedRule != null)
                ? results.Where(r => r.MatchedRule != null).Average(r => r.MatchedRule!.Confidence.Value)
                : 0
        };

        foreach (var result in results)
        {
            if (result.MatchedRule != null)
            {
                var fc = result.MatchedRule.Label ?? "Unknown";
                stats.FeatureClassCounts.TryGetValue(fc, out int fcCount);
                stats.FeatureClassCounts[fc] = fcCount + 1;

                var rule = result.MatchedRule.RuleName ?? "Unknown";
                stats.RuleUsageCounts.TryGetValue(rule, out int ruleCount);
                stats.RuleUsageCounts[rule] = ruleCount + 1;
            }
        }

        return stats;
    }
}
