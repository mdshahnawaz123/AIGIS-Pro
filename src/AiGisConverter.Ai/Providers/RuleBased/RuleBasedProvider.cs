using System.Diagnostics;
using System.Text;
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

    /// <summary>Subject metadata key carrying the stable BIM category enum name.</summary>
    public const string BuiltInCategoryKey = "BuiltInCategory";

    /// <summary>Subject metadata key carrying the BIM category display name.</summary>
    public const string CategoryKey = "Category";

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
            .Select(label => new LabelTokens(label, Tokenise(label, options), RemoveSeparators(label)))
            .ToList();

        List<ClassificationResult> results = new(request.Subjects.Count);
        HashSet<string> unmapped = new(StringComparer.OrdinalIgnoreCase);

        foreach (ClassificationSubject subject in request.Subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(Classify(subject, labels, request.Context, options, unmapped));
        }

        _logger.LogDebug("Rule-based provider classified {Count} subjects.", results.Count);

        // Named rather than counted. An operator who sees everything land in Unclassified needs to
        // know which rules are missing, and the alternative to configuring them is not a better
        // answer - it is the same wrong answer the category map exists to stop.
        if (unmapped.Count > 0)
        {
            _logger.LogInformation(
                "{Count} BIM categories have no feature class and were left unclassified: {Categories}. " +
                "Map them under Ai:Providers:rulebased:CategoryRules.",
                unmapped.Count,
                string.Join(", ", unmapped.Order(StringComparer.Ordinal)));
        }

        AIUsage usage = new(null, null, Stopwatch.GetElapsedTime(startedAt));
        return Task.FromResult(new AIClassificationResponse(results, Key, "lexicon-v1", usage));
    }

    private ClassificationResult Classify(
        ClassificationSubject subject,
        IReadOnlyList<LabelTokens> labels,
        ClassificationContext context,
        RuleBasedOptions options,
        HashSet<string> unmapped)
    {
        if (TryGetDeclaredCategory(subject, out string categoryKind, out string category))
        {
            return ClassifyByCategory(subject, categoryKind, category, labels, context, options, unmapped);
        }

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
                Confidence.Clamp(Score(subjectTokens, label) * options.MaximumSimilarityConfidence)))
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

    /// <summary>Reads the BIM category a subject declares, preferring the stable enum name.</summary>
    /// <remarks>
    /// <c>BuiltInCategory</c> and <c>Category</c> are written by the Revit reader and by no other
    /// reader, so their presence is what marks a subject as carrying authored category metadata
    /// rather than only a layer name.
    /// </remarks>
    private static bool TryGetDeclaredCategory(
        ClassificationSubject subject,
        out string kind,
        out string category)
    {
        if (subject.Metadata.TryGetValue(BuiltInCategoryKey, out string? builtIn) &&
            !string.IsNullOrWhiteSpace(builtIn))
        {
            kind = BuiltInCategoryKey;
            category = builtIn.Trim();
            return true;
        }

        if (subject.Metadata.TryGetValue(CategoryKey, out string? named) &&
            !string.IsNullOrWhiteSpace(named))
        {
            kind = CategoryKey;
            category = named.Trim();
            return true;
        }

        kind = string.Empty;
        category = string.Empty;
        return false;
    }

    /// <summary>Decides a subject that declares a BIM category, without consulting its name.</summary>
    /// <remarks>
    /// Deliberately total: every declared category either maps to a candidate label or is left
    /// unclassified. There is no fall-through to similarity scoring, because a category that the
    /// configuration does not account for is an unknown, and answering an unknown from the
    /// resemblance of its name is how a sun path became a footpath.
    /// </remarks>
    private ClassificationResult ClassifyByCategory(
        ClassificationSubject subject,
        string kind,
        string category,
        IReadOnlyList<LabelTokens> labels,
        ClassificationContext context,
        RuleBasedOptions options,
        HashSet<string> unmapped)
    {
        if (options.CategoryRules.TryGetValue(category, out string? mapped))
        {
            foreach (LabelTokens candidate in labels)
            {
                if (string.Equals(candidate.Label, mapped, StringComparison.OrdinalIgnoreCase))
                {
                    return new ClassificationResult(
                        subject.Id,
                        candidate.Label,
                        Confidence.Clamp(options.CategoryConfidence),
                        Key)
                    {
                        Rationale = $"{kind} '{category}' is mapped to '{candidate.Label}'.",
                    };
                }
            }

            _logger.LogWarning(
                "{Kind} '{Category}' maps to '{Label}', which is not a candidate feature class. " +
                "Leaving the subject unclassified.",
                kind,
                category,
                mapped);

            return new ClassificationResult(subject.Id, context.UnknownLabel, Confidence.Zero, Key)
            {
                Rationale =
                    $"{kind} '{category}' is mapped to '{mapped}', which is not one of the " +
                    "candidate feature classes.",
            };
        }

        // A candidate label that names the category exactly is not a guess, so it is accepted
        // without the map having to restate it. Anything looser is refused.
        foreach (LabelTokens candidate in labels)
        {
            if (string.Equals(candidate.Label, category, StringComparison.OrdinalIgnoreCase))
            {
                return new ClassificationResult(
                    subject.Id,
                    candidate.Label,
                    Confidence.Clamp(options.CategoryConfidence),
                    Key)
                {
                    Rationale = $"{kind} '{category}' names a candidate feature class exactly.",
                };
            }
        }

        unmapped.Add(category);

        return new ClassificationResult(subject.Id, context.UnknownLabel, Confidence.Zero, Key)
        {
            Rationale =
                $"{kind} '{category}' has no configured feature class. Add a category rule to " +
                "map it; its name is not evidence of what it is.",
        };
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

    /// <summary>Fraction of the label's vocabulary present in the subject's name.</summary>
    /// <remarks>
    /// <para>
    /// Matching is by whole token. Substring containment used to stand in for it, and that is what
    /// read <c>Sun Path</c> as <c>Footpath</c>: <c>footpath</c> was the label's only token,
    /// <c>path</c> sits inside it, so the label's entire vocabulary counted as matched and the
    /// subject scored 1.0. Nothing downstream can recover from a wrong answer delivered at full
    /// confidence, so the rule that produced it is gone rather than tuned.
    /// </para>
    /// <para>
    /// Containment was doing one job worth keeping: CAD layers routinely run a label's words
    /// together, and <c>WATERMAIN</c> does mean <c>Water Main</c>. That case is now handled
    /// exactly, by comparing the subject's token against the label with its separators removed, so
    /// the label's words have to account for the whole token. <c>DOMAIN</c> no longer matches
    /// <c>Main</c>, because <c>do</c> is left over and nothing explains it.
    /// </para>
    /// </remarks>
    private static double Score(IReadOnlySet<string> subjectTokens, LabelTokens label)
    {
        if (subjectTokens.Count == 0 || label.Tokens.Count == 0)
        {
            return 0d;
        }

        // A run-together spelling of the whole label matches all of it or none of it.
        foreach (string subjectToken in subjectTokens)
        {
            if (TokensMatch(subjectToken, label.Compact))
            {
                return 1d;
            }
        }

        int matches = 0;

        foreach (string labelToken in label.Tokens)
        {
            foreach (string subjectToken in subjectTokens)
            {
                if (TokensMatch(subjectToken, labelToken))
                {
                    matches++;
                    break;
                }
            }
        }

        return (double)matches / label.Tokens.Count;
    }

    /// <summary>Decides whether two name tokens denote the same thing.</summary>
    /// <remarks>
    /// Equality, plus the regular plural so that a <c>Roads</c> label still meets a <c>ROAD</c>
    /// layer. Nothing looser: every broader rule considered here admitted a compound whose meaning
    /// differs from the word inside it, which is the defect this replaced.
    /// </remarks>
    /// <param name="subjectToken">A token of the layer or element name.</param>
    /// <param name="labelToken">A token of the candidate feature class.</param>
    /// <returns><see langword="true"/> when the tokens denote the same thing.</returns>
    private static bool TokensMatch(string subjectToken, string labelToken)
    {
        return string.Equals(subjectToken, labelToken, StringComparison.OrdinalIgnoreCase)
            || IsRegularPluralOf(subjectToken, labelToken)
            || IsRegularPluralOf(labelToken, subjectToken);
    }

    /// <summary>Decides whether one token is the regular plural of another.</summary>
    /// <param name="plural">The candidate plural.</param>
    /// <param name="singular">The candidate singular.</param>
    /// <returns><see langword="true"/> when <paramref name="plural"/> is the plural.</returns>
    private static bool IsRegularPluralOf(string plural, string singular)
    {
        int added = plural.Length - singular.Length;

        if ((added != 1 && added != 2) ||
            !plural.StartsWith(singular, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string suffix = plural[singular.Length..];

        return string.Equals(suffix, "s", StringComparison.OrdinalIgnoreCase)
            || string.Equals(suffix, "es", StringComparison.OrdinalIgnoreCase);
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

    /// <summary>Strips every token separator, giving the run-together spelling of a label.</summary>
    private static string RemoveSeparators(string value)
    {
        StringBuilder builder = new(value.Length);

        foreach (char character in value)
        {
            if (Array.IndexOf(TokenSeparators, character) < 0)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return builder.ToString();
    }

    /// <summary>A candidate label with the forms the scorer compares against.</summary>
    /// <param name="Label">The candidate label, verbatim.</param>
    /// <param name="Tokens">The label's normalised tokens.</param>
    /// <param name="Compact">The label with its separators removed, lowercased.</param>
    private sealed record LabelTokens(string Label, IReadOnlySet<string> Tokens, string Compact);
}
