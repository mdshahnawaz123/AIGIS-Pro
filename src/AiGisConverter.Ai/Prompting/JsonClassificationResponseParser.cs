using System.Text.Json;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Ai.Prompting;

/// <summary>
/// Default <see cref="IClassificationResponseParser"/>. Tolerant by design: models wrap JSON in
/// code fences, add prose, echo unknown labels and emit percentages instead of probabilities.
/// </summary>
/// <remarks>
/// Parsing is deliberately defensive rather than strict. A single malformed entry degrades one
/// layer to unclassified; it does not fail an entire batch conversion.
/// </remarks>
public sealed class JsonClassificationResponseParser : IClassificationResponseParser
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    private readonly ILogger<JsonClassificationResponseParser> _logger;

    /// <summary>Initializes a new instance of the <see cref="JsonClassificationResponseParser"/> class.</summary>
    /// <param name="logger">Logger used to report malformed model output.</param>
    public JsonClassificationResponseParser(ILogger<JsonClassificationResponseParser> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<ClassificationResult> Parse(
        string content,
        IReadOnlyList<ClassificationSubject> subjects,
        ClassificationContext context,
        string providerKey)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(context);

        if (string.IsNullOrWhiteSpace(content))
        {
            _logger.LogWarning("Provider {ProviderKey} returned an empty response.", providerKey);
            return [];
        }

        string json = ExtractJsonObject(content);

        if (json.Length == 0)
        {
            _logger.LogWarning("Provider {ProviderKey} returned no JSON object.", providerKey);
            return [];
        }

        HashSet<string> allowedLabels = new(context.CandidateLabels, StringComparer.OrdinalIgnoreCase);
        Dictionary<string, ClassificationSubject> subjectsById =
            subjects.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);

        try
        {
            using JsonDocument document = JsonDocument.Parse(json, DocumentOptions);

            if (!TryGetResultsArray(document.RootElement, out JsonElement results))
            {
                _logger.LogWarning("Provider {ProviderKey} returned JSON without a results array.", providerKey);
                return [];
            }

            List<ClassificationResult> parsed = new(subjects.Count);

            foreach (JsonElement element in results.EnumerateArray())
            {
                if (TryParseEntry(element, subjectsById, allowedLabels, context, providerKey, out ClassificationResult? result))
                {
                    parsed.Add(result!);
                }
            }

            return parsed;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Provider {ProviderKey} returned unparseable JSON.", providerKey);
            return [];
        }
    }

    /// <summary>Extracts the outermost JSON object, discarding fences and prose.</summary>
    /// <param name="content">Raw assistant content.</param>
    /// <returns>The JSON substring, or an empty string when none is present.</returns>
    private static string ExtractJsonObject(string content)
    {
        int start = content.IndexOf('{', StringComparison.Ordinal);
        int end = content.LastIndexOf('}');

        return start >= 0 && end > start ? content[start..(end + 1)] : string.Empty;
    }

    private static bool TryGetResultsArray(JsonElement root, out JsonElement results)
    {
        if (root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("results", out results) &&
            results.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        results = default;
        return false;
    }

    private bool TryParseEntry(
        JsonElement element,
        IReadOnlyDictionary<string, ClassificationSubject> subjectsById,
        IReadOnlySet<string> allowedLabels,
        ClassificationContext context,
        string providerKey,
        out ClassificationResult? result)
    {
        result = null;

        if (element.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        string? id = ReadString(element, "id");

        if (id is null || !subjectsById.ContainsKey(id))
        {
            _logger.LogDebug("Provider {ProviderKey} returned an entry for unknown id '{Id}'.", providerKey, id);
            return false;
        }

        string label = NormaliseLabel(ReadString(element, "label"), allowedLabels, context.UnknownLabel);
        Confidence confidence = ReadConfidence(element, "confidence");

        result = new ClassificationResult(id, label, confidence, providerKey)
        {
            Rationale = ReadString(element, "rationale"),
        };

        if (element.TryGetProperty("alternatives", out JsonElement alternatives) &&
            alternatives.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement alternative in alternatives.EnumerateArray())
            {
                if (alternative.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string? rawLabel = ReadString(alternative, "label");

                if (rawLabel is null || !allowedLabels.Contains(rawLabel))
                {
                    continue;
                }

                result.AddAlternative(new ClassificationCandidate(
                    rawLabel,
                    ReadConfidence(alternative, "confidence")));
            }
        }

        return true;
    }

    /// <summary>Maps a returned label back onto the allowed set, falling back to the unknown label.</summary>
    private static string NormaliseLabel(string? raw, IReadOnlySet<string> allowedLabels, string unknownLabel)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return unknownLabel;
        }

        string trimmed = raw.Trim();
        return allowedLabels.Contains(trimmed) ? trimmed : unknownLabel;
    }

    /// <summary>Reads a confidence, accepting both probabilities and percentages.</summary>
    private static Confidence ReadConfidence(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out JsonElement value))
        {
            return Confidence.Zero;
        }

        double score = value.ValueKind switch
        {
            JsonValueKind.Number when value.TryGetDouble(out double number) => number,
            JsonValueKind.String when double.TryParse(
                value.GetString(),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double parsed) => parsed,
            _ => 0d,
        };

        // Models frequently answer 85 when asked for 0.85.
        if (score > 1d && score <= 100d)
        {
            score /= 100d;
        }

        return Confidence.Clamp(score);
    }

    private static string? ReadString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
