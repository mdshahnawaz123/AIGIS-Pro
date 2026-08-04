using System.Text;
using System.Text.Json.Serialization;

namespace AiGisConverter.Gis.Profiles;

/// <summary>How layer and field names are normalised for a target system.</summary>
public sealed class NamingRules
{
    /// <summary>Gets or sets the case convention applied to names.</summary>
    [JsonPropertyName("case")]
    public NameCase Case { get; set; } = NameCase.Preserve;

    /// <summary>Gets or sets the character substituted for anything not permitted.</summary>
    [JsonPropertyName("separator")]
    public string Separator { get; set; } = "_";

    /// <summary>
    /// Gets or sets the maximum field-name length. Zero means unlimited.
    /// </summary>
    /// <remarks>
    /// Shapefile's DBF header caps field names at ten characters. Truncating here, deliberately
    /// and visibly, is better than letting the driver do it silently and collide two fields.
    /// </remarks>
    [JsonPropertyName("maximumFieldNameLength")]
    public int MaximumFieldNameLength { get; set; }

    /// <summary>Gets or sets the maximum layer-name length. Zero means unlimited.</summary>
    [JsonPropertyName("maximumLayerNameLength")]
    public int MaximumLayerNameLength { get; set; }

    /// <summary>Gets or sets a value indicating whether non-ASCII characters are stripped.</summary>
    [JsonPropertyName("asciiOnly")]
    public bool AsciiOnly { get; set; }

    /// <summary>Gets or sets a prefix applied to every name.</summary>
    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    /// <summary>The name used when normalisation consumes every character of the original.</summary>
    public const string FallbackName = "layer";

    /// <summary>Applies the rules to a name.</summary>
    /// <param name="name">The name to normalise.</param>
    /// <param name="maximumLength">Length cap, or zero to use <see cref="MaximumLayerNameLength"/>.</param>
    /// <returns>The normalised name.</returns>
    public string Apply(string name, int maximumLength = 0)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        string working = string.IsNullOrEmpty(Prefix) ? name.Trim() : Prefix + name.Trim();
        StringBuilder builder = new(working.Length);

        foreach (char character in working)
        {
            bool permitted = char.IsLetterOrDigit(character) || character == '_';

            if (AsciiOnly && character > 127)
            {
                permitted = false;
            }

            builder.Append(permitted ? character : Separator);
        }

        string result = Collapse(builder.ToString());

        result = Case switch
        {
            NameCase.Upper => result.ToUpperInvariant(),
            NameCase.Lower => result.ToLowerInvariant(),
            _ => result,
        };

        // Collapsing and trimming can consume the whole name: "..." becomes separators, then
        // nothing. An empty segment turns Path.Combine(dir, name + ".geojson") into a hidden file
        // in the output folder rather than the layer the user asked for.
        if (result.Length == 0)
        {
            result = FallbackName;
        }

        int cap = maximumLength > 0 ? maximumLength : MaximumLayerNameLength;

        return cap > 0 && result.Length > cap ? result[..cap] : result;
    }

    /// <summary>Collapses runs of the separator and trims it from both ends.</summary>
    private string Collapse(string value)
    {
        if (string.IsNullOrEmpty(Separator))
        {
            return value;
        }

        string doubled = Separator + Separator;

        while (value.Contains(doubled, StringComparison.Ordinal))
        {
            value = value.Replace(doubled, Separator, StringComparison.Ordinal);
        }

        return value.Trim(Separator[0]);
    }
}

/// <summary>Case conventions a profile may impose.</summary>
public enum NameCase
{
    /// <summary>Leave the source casing alone.</summary>
    Preserve = 0,

    /// <summary>Force upper case.</summary>
    Upper = 1,

    /// <summary>Force lower case.</summary>
    Lower = 2,
}
