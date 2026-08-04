using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using AiGisConverter.Business.Classification;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.MappingEditor.Business;

public class SimulationResult
{
    public SourceElement Element { get; }
    public IReadOnlyList<ClassificationCandidate> Candidates { get; }
    public TimeSpan ExecutionTime { get; }

    public ClassificationCandidate? MatchedRule => Candidates.FirstOrDefault();
    public IReadOnlyList<ClassificationCandidate> RejectedRules => Candidates.Skip(1).ToList();

    public SimulationResult(SourceElement element, IReadOnlyList<ClassificationCandidate> candidates, TimeSpan executionTime)
    {
        Element = element;
        Candidates = candidates;
        ExecutionTime = executionTime;
    }
}

public class RuleSimulator
{
    public IReadOnlyList<SimulationResult> Simulate(MappingProfile profile, IEnumerable<SourceElement> elements)
    {
        var engine = new ClassificationEngine();
        engine.AddProfile(profile);

        var results = new List<SimulationResult>();

        foreach (var element in elements)
        {
            var sw = Stopwatch.StartNew();
            var candidates = engine.Evaluate(element);
            sw.Stop();

            // The ClassificationEngine returns candidates sorted by priority.
            results.Add(new SimulationResult(element, candidates, sw.Elapsed));
        }

        return results;
    }
}
