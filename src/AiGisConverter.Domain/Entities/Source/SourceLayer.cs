namespace AiGisConverter.Domain.Entities.Source;

/// <summary>
/// A grouping of elements within a source document: a CAD layer, an IFC storey, a Revit category
/// or a point-cloud classification class.
/// </summary>
public sealed class SourceLayer
{
    private readonly List<SourceElement> _elements = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="SourceLayer"/> class.</summary>
    /// <param name="name">The layer name as it appears in the source.</param>
    public SourceLayer(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>Gets the layer name as it appears in the source.</summary>
    public string Name { get; }

    /// <summary>Gets or sets a value indicating whether the layer is visible in the source document.</summary>
    public bool IsVisible { get; set; } = true;

    /// <summary>Gets the elements belonging to this layer.</summary>
    public IReadOnlyList<SourceElement> Elements => _elements;

    /// <summary>Gets layer-level metadata, for example colour, line type or an IFC type name.</summary>
    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    /// <summary>Adds an element to the layer.</summary>
    /// <param name="element">The element to add.</param>
    public void AddElement(SourceElement element)
    {
        ArgumentNullException.ThrowIfNull(element);

        _elements.Add(element);
    }

    /// <summary>Adds several elements to the layer.</summary>
    /// <param name="elements">The elements to add.</param>
    public void AddElements(IEnumerable<SourceElement> elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        _elements.AddRange(elements);
    }

    /// <summary>Sets a metadata value, replacing any existing value for the key.</summary>
    /// <param name="key">The metadata key, matched case-insensitively.</param>
    /// <param name="value">The value.</param>
    public void SetMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _metadata[key] = value;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({_elements.Count} elements)";
}
