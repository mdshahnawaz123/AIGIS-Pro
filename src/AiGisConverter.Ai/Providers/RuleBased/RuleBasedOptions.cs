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

    /// <summary>Gets or sets the confidence reported for a match on a declared BIM category.</summary>
    /// <remarks>
    /// Higher than <see cref="KeywordConfidence"/> because the evidence is different in kind: a
    /// category is a fact the authoring application records, not an inference drawn from a name.
    /// </remarks>
    public double CategoryConfidence { get; set; } = 0.95d;

    /// <summary>
    /// Gets the category-to-label map, for example <c>"OST_Roads" : "Carriageway"</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Keys are matched, case-insensitively and in full, against the subject's
    /// <c>BuiltInCategory</c> and then its <c>Category</c>. Both are written by the Revit reader
    /// and by nothing else, so this map governs BIM elements and leaves CAD layers untouched.
    /// </para>
    /// <para>
    /// A subject that declares a category is decided here or not at all: an unmapped category
    /// yields <c>Unclassified</c> rather than falling through to name similarity. That is the
    /// point of the map. Similarity scoring once read "Sun Path" as "Footpath" and "Pipe Segments"
    /// as "Stormwater Pipe" at working confidence, and no threshold distinguishes those from a
    /// correct answer, because the evidence genuinely looks the same.
    /// </para>
    /// </remarks>
    public IDictionary<string, string> CategoryRules { get; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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
