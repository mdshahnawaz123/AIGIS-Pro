using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.Entities.Ai;

/// <summary>
/// A provider-agnostic description of one thing to be classified &#8212; normally a CAD layer,
/// optionally a block definition or an entity group.
/// </summary>
/// <remarks>
/// This type deliberately contains no geometry and no CAD handles. It is the narrow, serialisable
/// projection that any AI provider &#8212; local model or remote language model &#8212; can reason
/// about, which is what keeps a prompt small enough to be affordable and a feature vector small
/// enough to be fast.
/// </remarks>
public sealed class ClassificationSubject
{
    private readonly Dictionary<GeometryKind, int> _geometryProfile = [];
    private readonly List<string> _sampleTexts = [];
    private readonly List<string> _blockNames = [];
    private readonly Dictionary<string, string> _metadata = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="ClassificationSubject"/> class.</summary>
    /// <param name="id">Stable identifier used to correlate the subject with its result.</param>
    /// <param name="name">The CAD layer or block name.</param>
    public ClassificationSubject(string id, string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Id = id;
        Name = name;
    }

    /// <summary>Gets the stable identifier used to correlate this subject with its result.</summary>
    public string Id { get; }

    /// <summary>Gets the CAD layer or block name, verbatim.</summary>
    public string Name { get; }

    /// <summary>Gets the number of source elements carried by the subject.</summary>
    public int EntityCount { get; private set; }

    /// <summary>Gets the geometry families observed within the subject, with their element counts.</summary>
    public IReadOnlyDictionary<GeometryKind, int> GeometryProfile => _geometryProfile;

    /// <summary>Gets a bounded sample of text values found on the subject, used as a classification signal.</summary>
    public IReadOnlyList<string> SampleTexts => _sampleTexts;

    /// <summary>Gets the distinct block names referenced by the subject.</summary>
    public IReadOnlyList<string> BlockNames => _blockNames;

    /// <summary>Gets free-form key/value hints, for example line type, colour or source file name.</summary>
    public IReadOnlyDictionary<string, string> Metadata => _metadata;

    /// <summary>Sets the number of source elements carried by the subject.</summary>
    /// <param name="count">The element count.</param>
    public void SetEntityCount(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        EntityCount = count;
    }

    /// <summary>Records elements of a geometry family.</summary>
    /// <param name="kind">The geometry family.</param>
    /// <param name="count">How many elements of that family were seen.</param>
    public void AddGeometry(GeometryKind kind, int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);

        _geometryProfile[kind] = _geometryProfile.GetValueOrDefault(kind) + count;
    }

    /// <summary>Adds a sample text value, ignoring blanks and duplicates.</summary>
    /// <param name="text">The text to add.</param>
    public void AddSampleText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        string trimmed = text.Trim();

        if (!_sampleTexts.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            _sampleTexts.Add(trimmed);
        }
    }

    /// <summary>Adds a referenced block name, ignoring blanks and duplicates.</summary>
    /// <param name="blockName">The block name to add.</param>
    public void AddBlockName(string blockName)
    {
        if (string.IsNullOrWhiteSpace(blockName))
        {
            return;
        }

        string trimmed = blockName.Trim();

        if (!_blockNames.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
        {
            _blockNames.Add(trimmed);
        }
    }

    /// <summary>Sets a metadata hint, replacing any existing value for the key.</summary>
    /// <param name="key">The hint key, matched case-insensitively.</param>
    /// <param name="value">The value.</param>
    public void SetMetadata(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _metadata[key] = value;
    }

    /// <summary>Gets the dominant geometry family, or <see cref="GeometryKind.Unknown"/> when empty.</summary>
    /// <returns>The geometry family with the highest element count.</returns>
    public GeometryKind GetDominantGeometry()
    {
        GeometryKind dominant = GeometryKind.Unknown;
        int best = -1;

        foreach (KeyValuePair<GeometryKind, int> pair in _geometryProfile)
        {
            if (pair.Value > best)
            {
                best = pair.Value;
                dominant = pair.Key;
            }
        }

        return dominant;
    }

    /// <inheritdoc />
    public override string ToString() => $"{Name} ({EntityCount} elements)";
}
