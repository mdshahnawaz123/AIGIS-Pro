using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.Services;

/// <summary>
/// Reduces a source layer to the narrow projection a classifier needs.
/// </summary>
/// <remarks>
/// <para>
/// This is the join between the source model and the AI model, and it belongs in the domain
/// because it encodes a judgement about what actually predicts a feature class: the layer name,
/// the dominant geometry, the block names, and a small sample of text. Not the geometry itself.
/// </para>
/// <para>
/// The sample is bounded deliberately. Sending a thousand text values to a language model costs
/// tokens and context without improving the answer, and it is what turns a classification pass
/// over a large drawing from seconds into minutes.
/// </para>
/// </remarks>
public static class ClassificationSubjectFactory
{
    private const int DefaultSampleTextLimit = 8;
    private const int DefaultBlockNameLimit = 8;

    /// <summary>Builds a subject from a source element.</summary>
    /// <param name="element">The element to describe.</param>
    /// <param name="layerName">The layer name the element belongs to.</param>
    /// <returns>The subject.</returns>
    public static ClassificationSubject FromElement(SourceElement element, string layerName)
    {
        ArgumentNullException.ThrowIfNull(element);

        ClassificationSubject subject = new(element.Id, layerName);
        subject.SetEntityCount(1);
        subject.AddGeometry(element.GeometryKind, 1);

        if (!string.IsNullOrWhiteSpace(element.Text))
        {
            subject.AddSampleText(element.Text);
        }

        if (element.NativeType is not null && element.NativeType.Contains("BLOCK", StringComparison.OrdinalIgnoreCase))
        {
            if (element.Attributes.TryGetValue("BlockName", out object? blockName) && blockName is string name && !string.IsNullOrWhiteSpace(name))
            {
                subject.AddBlockName(name);
            }
        }

        foreach (var attribute in element.Attributes)
        {
            if (attribute.Value != null)
            {
                subject.SetMetadata(attribute.Key, attribute.Value.ToString() ?? string.Empty);
            }
        }

        return subject;
    }

    /// <summary>Builds a subject from a source layer.</summary>
    /// <param name="layer">The layer to describe.</param>
    /// <param name="sampleTextLimit">Maximum distinct text values to include.</param>
    /// <param name="blockNameLimit">Maximum distinct block names to include.</param>
    /// <returns>The subject.</returns>
    public static ClassificationSubject FromLayer(
        SourceLayer layer,
        int sampleTextLimit = DefaultSampleTextLimit,
        int blockNameLimit = DefaultBlockNameLimit)
    {
        ArgumentNullException.ThrowIfNull(layer);

        ClassificationSubject subject = new(layer.Name, layer.Name);
        subject.SetEntityCount(layer.Elements.Count);

        Dictionary<GeometryKind, int> profile = [];
        HashSet<string> texts = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> blocks = new(StringComparer.OrdinalIgnoreCase);

        foreach (SourceElement element in layer.Elements)
        {
            profile[element.GeometryKind] = profile.GetValueOrDefault(element.GeometryKind) + 1;

            if (!string.IsNullOrWhiteSpace(element.Text) && texts.Count < sampleTextLimit)
            {
                texts.Add(element.Text.Trim());
            }

            if (element.NativeType is not null &&
                blocks.Count < blockNameLimit &&
                element.NativeType.Contains("BLOCK", StringComparison.OrdinalIgnoreCase) &&
                element.Attributes.TryGetValue("BlockName", out object? blockName) &&
                blockName is string name &&
                !string.IsNullOrWhiteSpace(name))
            {
                blocks.Add(name.Trim());
            }
        }

        foreach (KeyValuePair<GeometryKind, int> pair in profile)
        {
            subject.AddGeometry(pair.Key, pair.Value);
        }

        foreach (string text in texts)
        {
            subject.AddSampleText(text);
        }

        foreach (string block in blocks)
        {
            subject.AddBlockName(block);
        }

        foreach (KeyValuePair<string, string> pair in layer.Metadata)
        {
            subject.SetMetadata(pair.Key, pair.Value);
        }

        return subject;
    }

    /// <summary>Builds one subject per layer in a document.</summary>
    /// <param name="document">The document to describe.</param>
    /// <param name="includeHiddenLayers">Whether layers hidden in the source are included.</param>
    /// <returns>The subjects, in document order.</returns>
    public static IReadOnlyList<ClassificationSubject> FromDocument(
        SourceDocument document,
        bool includeHiddenLayers = false)
    {
        ArgumentNullException.ThrowIfNull(document);

        return document.Layers
            .Where(layer => includeHiddenLayers || layer.IsVisible)
            .Where(static layer => layer.Elements.Count > 0)
            .Select(layer => FromLayer(layer))
            .ToList();
    }
}
