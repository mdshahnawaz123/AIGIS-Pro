using System.Diagnostics;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Providers.RuleBased;

/// <summary>
/// Deterministic classifier based on token similarity between the CAD layer name and the
/// candidate labels, plus configurable keyword rules.
/// </summary>
/// <remarks>
/// <para>
/// This provider exists so the application is fully functional with no model, no endpoint and no
/// network. It is the default value of <c>Ai:ActiveProvider</c> and the default fallback, which
/// means an unreachable Ollama server degrades conversion quality rather than breaking it.
/// </para>
/// <para>
/// It is also the reference implementation of <see cref="IAIProvider"/>: it demonstrates that the
/// contract carries no assumption of a language model.
/// </para>
/// </remarks>
public sealed class RuleBasedProvider : IAIProvider
{
    /// <summary>The configuration key and provider key for this provider.</summary>
    public const string ProviderKey = "rulebased";

    private static readonly char[] TokenSeparators =
        [' ', '-', '_', '.', ':', '/', '\\', '|', '(', ')', '[', ']', '+', ',', '#', '*', '\t'];

    private static readonly IReadOnlyDictionary<string, string> DefaultAbbreviations =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["bldg"] = "building",
            ["bld"] = "building",
            ["rd"] = "road",
            ["hwy"] = "highway",
            ["ctr"] = "centre",
            ["cl"] = "centreline",
            ["bdy"] = "boundary",
            ["bnd"] = "boundary",
            ["parc"] = "parcel",
            ["wtr"] = "water",
            ["swr"] = "sewer",
            ["storm"] = "stormwater",
            ["elec"] = "electrical",
            ["telco"] = "telecommunication",
            ["comm"] = "telecommunication",
            ["veg"] = "vegetation",
            ["topo"] = "topography",
            ["cont"] = "contour",
            ["ctour"] = "contour",
            ["pvmt"] = "pavement",
            ["kerb"] = "curb",
            ["fnc"] = "fence",
            ["utl"] = "utility",
            ["mh"] = "manhole",
            ["pt"] = "point",
            ["txt"] = "text",
            ["anno"] = "annotation",
        };

    private readonly IOptionsMonitor<RuleBasedOptions> _options;
    private readonly ILogger<RuleBasedProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="RuleBasedProvider"/> class.</summary>
    /// <param name="options">Live provider options.</param>
    /// <param name="logger">Logger for the provider.</param>
    public RuleBasedProvider(IOptionsMonitor<RuleBasedOptions> options, ILogger<RuleBasedProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Key => ProviderKey;

    /// <inheritdoc />
    public AIProviderMetadata Metadata { get; } = new(
        ProviderKey,
        "Rule-based (offline)",
        AIProviderKind.Deterministic,
        MaxSubjectsPerCall: int.MaxValue,
        SupportsRationale: true,
        RequiresNetwork: false);

    /// <inheritdoc />
    public Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(AIProviderAvailability.Available("built-in lexicon"));

    /// <inheritdoc />
    public Task<AIClassificationResponse> ClassifyAsync(
        AIClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startedAt = Stopwatch.GetTimestamp();
        RuleBasedOptions options = _options.CurrentValue;

        IReadOnlyList<LabelTokens> labels = request.Context.CandidateLabels
            .Select(label => new LabelTokens(label, Tokenise(label, options)))
            .ToList();

        List<ClassificationResult> results = new(request.Subjects.Count);

        foreach (ClassificationSubject subject in request.Subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(Classify(subject, labels, request.Context, options));
        }

        _logger.LogDebug("Rule-based provider classified {Count} subjects.", results.Count);

        AIUsage usage = new(null, null, Stopwatch.GetElapsedTime(startedAt));
        return Task.FromResult(new AIClassificationResponse(results, Key, "lexicon-v1", usage));
    }

    private ClassificationResult Classify(
        ClassificationSubject subject,
        IReadOnlyList<LabelTokens> labels,
        ClassificationContext context,
        RuleBasedOptions options)
    {
        IReadOnlySet<string> subjectTokens = Tokenise(subject.Name, options);

        if (TryMatchKeywordRule(subjectTokens, labels, options, out string? ruleLabel))
        {
            return new ClassificationResult(
                subject.Id,
                ruleLabel!,
                Confidence.Clamp(options.KeywordConfidence),
                Key)
            {
                Rationale = "Matched an explicitly configured keyword rule.",
            };
        }

        List<ClassificationCandidate> scored = labels
            .Select(label => new ClassificationCandidate(
                label.Label,
                Confidence.Clamp(Score(subjectTokens, label.Tokens) * options.MaximumSimilarityConfidence)))
            .Where(candidate => candidate.Confidence.Value > 0d)
            .OrderByDescending(candidate => candidate.Confidence.Value)
            .ThenBy(candidate => candidate.Label, StringComparer.Ordinal)
            .Take(4)
            .ToList();

        if (scored.Count == 0 || scored[0].Confidence.Value < options.MinimumScore)
        {
            return new ClassificationResult(subject.Id, context.UnknownLabel, Confidence.Zero, Key)
            {
                Rationale = "No candidate label shared enough vocabulary with the layer name.",
            };
        }

        ClassificationResult result = new(subject.Id, scored[0].Label, scored[0].Confidence, Key)
        {
            Rationale = $"Layer name tokens overlap the label '{scored[0].Label}'.",
        };

        for (int i = 1; i < scored.Count; i++)
        {
            result.AddAlternative(scored[i]);
        }

        return result;
    }

    private static bool TryMatchKeywordRule(
        IReadOnlySet<string> subjectTokens,
        IReadOnlyList<LabelTokens> labels,
        RuleBasedOptions options,
        out string? label)
    {
        foreach (KeyValuePair<string, string> rule in options.KeywordRules)
        {
            if (!subjectTokens.Contains(rule.Key))
            {
                continue;
            }

            foreach (LabelTokens candidate in labels)
            {
                if (string.Equals(candidate.Label, rule.Value, StringComparison.OrdinalIgnoreCase))
                {
                    label = candidate.Label;
                    return true;
                }
            }
        }

        label = null;
        return false;
    }

    /// <summary>Jaccard-style overlap weighted towards covering the label's vocabulary.</summary>
    private static double Score(IReadOnlySet<string> subjectTokens, IReadOnlySet<string> labelTokens)
    {
        if (subjectTokens.Count == 0 || labelTokens.Count == 0)
        {
            return 0d;
        }

        int matches = 0;

        foreach (string labelToken in labelTokens)
        {
            foreach (string subjectToken in subjectTokens)
            {
                if (string.Equals(labelToken, subjectToken, StringComparison.OrdinalIgnoreCase) ||
                    (labelToken.Length >= 4 && subjectToken.Contains(labelToken, StringComparison.OrdinalIgnoreCase)) ||
                    (subjectToken.Length >= 4 && labelToken.Contains(subjectToken, StringComparison.OrdinalIgnoreCase)))
                {
                    matches++;
                    break;
                }
            }
        }

        return (double)matches / labelTokens.Count;
    }

    /// <summary>Splits a name into normalised tokens, expanding known abbreviations.</summary>
    private static IReadOnlySet<string> Tokenise(string value, RuleBasedOptions options)
    {
        HashSet<string> tokens = new(StringComparer.OrdinalIgnoreCase);

        foreach (string raw in value.Split(TokenSeparators, StringSplitOptions.RemoveEmptyEntries))
        {
            string token = raw.Trim().ToLowerInvariant();

            if (token.Length == 0)
            {
                continue;
            }

            tokens.Add(token);

            if (options.Abbreviations.TryGetValue(token, out string? configured))
            {
                tokens.Add(configured.ToLowerInvariant());
            }
            else if (DefaultAbbreviations.TryGetValue(token, out string? builtIn))
            {
                tokens.Add(builtIn);
            }
        }

        return tokens;
    }

    private sealed record LabelTokens(string Label, IReadOnlySet<string> Tokens);
}
