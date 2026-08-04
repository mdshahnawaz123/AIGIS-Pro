namespace AiGisConverter.Domain.Entities.Source;

/// <summary>
/// The format-neutral result of reading a source. Every reader plugin, whatever it reads, produces
/// one of these; everything downstream consumes only this.
/// </summary>
public sealed class SourceDocument
{
    private readonly List<SourceLayer> _layers = [];
    private readonly List<string> _warnings = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="SourceDocument"/> class.</summary>
    /// <param name="origin">The reference that was read.</param>
    /// <param name="formatKey">The reader's format key, for example <c>dwg</c> or <c>ifc</c>.</param>
    public SourceDocument(SourceReference origin, string formatKey)
    {
        ArgumentNullException.ThrowIfNull(origin);
        ArgumentException.ThrowIfNullOrWhiteSpace(formatKey);

        Origin = origin;
        FormatKey = formatKey;
    }

    /// <summary>Gets the reference that was read.</summary>
    public SourceReference Origin { get; }

    /// <summary>Gets the reader's format key.</summary>
    public string FormatKey { get; }

    /// <summary>
    /// Gets or sets the coordinate reference system declared by the source, as an authority code
    /// such as <c>EPSG:27700</c>. Null when the source declares none and detection must run.
    /// </summary>
    public string? DeclaredCrs { get; set; }

    /// <summary>Gets or sets the linear units declared by the source, for example <c>metre</c>.</summary>
    public string? Units { get; set; }

    /// <summary>Gets the layers read from the source.</summary>
    public IReadOnlyList<SourceLayer> Layers => _layers;

    /// <summary>Gets document-level metadata, for example author, application and version.</summary>
    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    /// <summary>
    /// Gets non-fatal problems encountered while reading, surfaced to the QA/QC report.
    /// </summary>
    /// <remarks>
    /// A reader that skips a malformed entity records it here rather than throwing. Silently
    /// dropping data is the one behaviour a conversion tool must never have.
    /// </remarks>
    public IReadOnlyList<string> Warnings => _warnings;

    /// <summary>Adds a layer to the document.</summary>
    /// <param name="layer">The layer to add.</param>
    public void AddLayer(SourceLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        _layers.Add(layer);
    }

    /// <summary>Finds a layer by name, case-insensitively.</summary>
    /// <param name="name">The layer name.</param>
    /// <returns>The layer, or <see langword="null"/> when the document has no such layer.</returns>
    public SourceLayer? FindLayer(string name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : _layers.Find(layer => string.Equals(layer.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Finds a layer by name, adding it when it is not yet present.</summary>
    /// <param name="name">The layer name.</param>
    /// <returns>The existing or newly created layer.</returns>
    public SourceLayer GetOrAddLayer(string name)
    {
        SourceLayer? existing = FindLayer(name);

        if (existing is not null)
        {
            return existing;
        }

        SourceLayer created = new(name);
        _layers.Add(created);

        return created;
    }

    /// <summary>Records a non-fatal problem encountered while reading.</summary>
    /// <param name="warning">The problem, phrased for the operator.</param>
    public void AddWarning(string warning)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(warning);

        _warnings.Add(warning);
    }

    /// <summary>Sets a metadata value, replacing any existing value for the key.</summary>
    /// <param name="key">The metadata key, matched case-insensitively.</param>
    /// <param name="value">The value.</param>
    public void SetMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _metadata[key] = value;
    }

    /// <summary>Gets the total element count across all layers.</summary>
    /// <returns>The number of elements read.</returns>
    public int CountElements() => _layers.Sum(static layer => layer.Elements.Count);

    /// <inheritdoc />
    public override string ToString() =>
        $"{FormatKey}:{Origin.Location} ({_layers.Count} layers, {CountElements()} elements)";
}
