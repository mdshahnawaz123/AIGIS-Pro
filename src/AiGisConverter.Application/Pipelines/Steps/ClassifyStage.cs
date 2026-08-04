using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Services;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System;

namespace AiGisConverter.Application.Pipelines.Steps;

/// <summary>
/// Assigns a feature class to each source element, using rules first and falling back to AI for ambiguities.
/// </summary>
public sealed class ClassifyStage : IPipelineStage
{
    private readonly IRuleEngine _ruleEngine;
    private readonly IAiClassifier _classifier;
    private readonly ILogger<ClassifyStage> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClassifyStage"/> class.
    /// </summary>
    /// <param name="ruleEngine">The rule engine to evaluate initial classifications.</param>
    /// <param name="classifier">The AI classifier to use for ambiguous entities.</param>
    /// <param name="logger">The logger for this stage.</param>
    public ClassifyStage(IRuleEngine ruleEngine, IAiClassifier classifier, ILogger<ClassifyStage> logger)
    {
        ArgumentNullException.ThrowIfNull(ruleEngine);
        ArgumentNullException.ThrowIfNull(classifier);
        ArgumentNullException.ThrowIfNull(logger);

        _ruleEngine = ruleEngine;
        _classifier = classifier;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Classify entities";

    /// <inheritdoc />
    public int Order => 300;

    /// <inheritdoc />
    public bool IsOptional => true;

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Document is null)
        {
            return Result.Failure(new Error("Pipeline.NoDocument", "No document was read."));
        }

        var elements = context.Document.Layers.SelectMany(l => l.Elements).ToList();
        if (elements.Count == 0)
        {
            return Result.Success();
        }

        int rulesMatched = 0;
        int accepted = 0;
        int belowThreshold = 0;

        List<ClassificationSubject> ambiguousSubjects = new();
        Dictionary<string, SourceElement> elementLookup = new(StringComparer.OrdinalIgnoreCase);

        foreach (var layer in context.Document.Layers)
        {
            foreach (var element in layer.Elements)
            {
                elementLookup[element.Id] = element;
                var candidates = _ruleEngine.Evaluate(element);

                bool useAi = false;
                ClassificationCandidate? bestCandidate = null;

                if (candidates.Count == 0)
                {
                    useAi = true;
                }
                else if (candidates.Count == 1)
                {
                    bestCandidate = candidates[0];
                }
                else
                {
                    // Multiple candidates. Check for a tie at the top.
                    var topCandidate = candidates[0];
                    var secondCandidate = candidates[1];

                    // If the delta in confidence is very small (e.g., < 0.05), it's a tie.
                    if (topCandidate.Confidence.Value - secondCandidate.Confidence.Value < 0.05)
                    {
                        useAi = true;
                    }
                    else
                    {
                        bestCandidate = topCandidate;
                    }
                }

                if (useAi)
                {
                    ambiguousSubjects.Add(ClassificationSubjectFactory.FromElement(element, layer.Name));
                }
                else if (bestCandidate != null)
                {
                    var result = new ClassificationResult(
                        element.Id, 
                        bestCandidate.Label, 
                        bestCandidate.Confidence, 
                        "RuleEngine")
                    {
                        RuleName = bestCandidate.RuleName,
                        Rationale = bestCandidate.Reason
                    };
                    
                    // Add alternatives
                    for (int i = 1; i < candidates.Count; i++)
                    {
                        result.AddAlternative(candidates[i]);
                    }
                    
                    result.MarkAccepted(true);
                    context.AssignClass(element.Id, result);
                    rulesMatched++;
                }
            }
        }

        int unclassified = elements.Count - rulesMatched;

        if (ambiguousSubjects.Count > 0)
        {
            ClassificationContext classification = new(CandidateLabels(context.Settings));
            
            Result<IReadOnlyList<ClassificationResult>> results = await _classifier
                .ClassifyAsync(ambiguousSubjects, classification, cancellationToken)
                .ConfigureAwait(false);

            if (results.IsSuccess)
            {
                foreach (ClassificationResult result in results.Value)
                {
                    if (elementLookup.TryGetValue(result.SubjectId, out var element))
                    {
                        context.AssignClass(element.Id, result);
                        
                        if (result.IsAccepted)
                        {
                            accepted++;
                        }
                        else
                        {
                            belowThreshold++;
                        }
                    }
                }
                unclassified -= results.Value.Count;
            }
            else
            {
                _logger.LogWarning("AI Classification fallback failed: {Error}", results.Error.Message);
            }
        }

        // Add any remaining unclassified entities explicitly
        foreach (var element in elements)
        {
            if (!context.EntityClassifications.ContainsKey(element.Id))
            {
                var result = new ClassificationResult(element.Id, FeatureClass.UnclassifiedName, Confidence.Zero, "Fallback");
                context.AssignClass(element.Id, result);
            }
        }

        context.Run.RecordClassification(rulesMatched + accepted, belowThreshold, unclassified);

        _logger.LogInformation(
            "Classified {RulesMatched} by rules, {Accepted} by AI, {BelowThreshold} below AI threshold, {Unclassified} not answered.",
            rulesMatched,
            accepted,
            belowThreshold,
            unclassified);

        return Result.Success();
    }

    private static IReadOnlyList<string> CandidateLabels(ConversionSettings settings)
    {
        List<string> labels = [.. settings.CandidateFeatureClasses];

        if (!labels.Contains(FeatureClass.UnclassifiedName, StringComparer.OrdinalIgnoreCase))
        {
            labels.Add(FeatureClass.UnclassifiedName);
        }

        return labels;
    }
}
