namespace AiGisConverter.Domain.Entities.Source;

/// <summary>
/// Identifies something to be read: a file on disk, a folder of tiles, or a live host-application
/// session such as an open Revit document.
/// </summary>
public sealed class SourceReference
{
    private readonly Dictionary<string, string> _hints = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="SourceReference"/> class.</summary>
    /// <param name="location">File path, folder path, or an opaque handle understood by the reader.</param>
    public SourceReference(string location)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(location);
        Location = location;
    }

    /// <summary>Gets the file path, folder path or opaque handle.</summary>
    public string Location { get; }

    /// <summary>Gets the lower-case file extension including the leading dot, or an empty string.</summary>
    public string Extension => Path.GetExtension(Location).ToLowerInvariant();

    /// <summary>
    /// Gets or sets a value indicating whether the reference points at a live host-application
    /// session rather than a file. Host-bound readers use this to choose the bridge transport.
    /// </summary>
    public bool IsLiveSession { get; set; }

    /// <summary>Gets reader-specific hints, for example a Revit view identifier or a LAS class filter.</summary>
    public IReadOnlyDictionary<string, string> Hints => _hints;

    /// <summary>Sets a reader hint, replacing any existing value for the key.</summary>
    /// <param name="key">The hint key, matched case-insensitively.</param>
    /// <param name="value">The value.</param>
    public void SetHint(string key, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        _hints[key] = value;
    }

    /// <inheritdoc />
    public override string ToString() => Location;
}
