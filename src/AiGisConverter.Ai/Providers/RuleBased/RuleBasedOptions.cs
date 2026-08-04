namespace AiGisConverter.Ai.Providers.RuleBased;

/// <summary>
/// Options for <see cref="RuleBasedProvider"/>, bound from <c>Ai:Providers:rulebased</c>.
/// </summary>
public sealed class RuleBasedOptions
{
    /// <summary>Gets or sets the confidence reported for an explicit keyword hit.</summary>
    public double KeywordConfidence { get; set; } = 0.80d;

    /// <summary>Gets or sets the maximum confidence reported for a token-similarity match.</summary>
    public double MaximumSimilarityConfidence { get; set; } = 0.70d;

    /// <summary>Gets or sets the minimum score below which the subject is left unclassified.</summary>
    public double MinimumScore { get; set; } = 0.25d;

    /// <summary>
    /// Gets the keyword-to-label overrides, for example <c>"wtr" : "Water Main"</c>. Keys are
    /// matched case-insensitively against tokens of the layer name and take precedence over
    /// similarity scoring, which lets a site standard be encoded without touching code.
    /// </summary>
    public IDictionary<string, string> KeywordRules { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Gets the abbreviation expansions applied before scoring, for example <c>"bldg" : "building"</c>.
    /// </summary>
    public IDictionary<string, string> Abbreviations { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
