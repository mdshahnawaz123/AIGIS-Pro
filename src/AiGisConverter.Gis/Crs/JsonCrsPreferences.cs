using System.Text.Json;
using AiGisConverter.Gis.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Gis.Crs;

/// <summary>
/// <see cref="ICrsPreferences"/> stored as a small JSON file under <c>%LOCALAPPDATA%</c>.
/// </summary>
/// <remarks>
/// Loaded once on construction and written on demand. Every file operation is guarded: a corrupt or
/// unreadable preferences file must degrade to an empty history, never prevent the application from
/// starting.
/// </remarks>
public sealed class JsonCrsPreferences : ICrsPreferences
{
    private const int MaxRecent = 20;

    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger<JsonCrsPreferences> _logger;
    private readonly object _gate = new();

    private PreferencesFile _state = new();

    /// <summary>Initializes a new instance of the <see cref="JsonCrsPreferences"/> class.</summary>
    /// <param name="logger">Logger for the store.</param>
    public JsonCrsPreferences(ILogger<JsonCrsPreferences> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;

        _path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiGisConverter",
            "crs-preferences.json");

        Load();
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Recent
    {
        get
        {
            lock (_gate)
            {
                return [.. _state.Recent];
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Favourites
    {
        get
        {
            lock (_gate)
            {
                return [.. _state.Favourites];
            }
        }
    }

    /// <inheritdoc />
    public string? LastInput
    {
        get { lock (_gate) { return _state.LastInput; } }
        set { lock (_gate) { _state.LastInput = value; } }
    }

    /// <inheritdoc />
    public string? LastOutput
    {
        get { lock (_gate) { return _state.LastOutput; } }
        set { lock (_gate) { _state.LastOutput = value; } }
    }

    /// <inheritdoc />
    public string? ProjectDefault
    {
        get { lock (_gate) { return _state.ProjectDefault; } }
        set { lock (_gate) { _state.ProjectDefault = value; } }
    }

    /// <inheritdoc />
    public string? CompanyDefault
    {
        get { lock (_gate) { return _state.CompanyDefault; } }
        set { lock (_gate) { _state.CompanyDefault = value; } }
    }

    /// <inheritdoc />
    public void RecordUse(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return;
        }

        lock (_gate)
        {
            _state.Recent.RemoveAll(entry => string.Equals(entry, identifier, StringComparison.OrdinalIgnoreCase));
            _state.Recent.Insert(0, identifier);

            if (_state.Recent.Count > MaxRecent)
            {
                _state.Recent.RemoveRange(MaxRecent, _state.Recent.Count - MaxRecent);
            }
        }

        Save();
    }

    /// <inheritdoc />
    public bool ToggleFavourite(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        bool isFavourite;

        lock (_gate)
        {
            int removed = _state.Favourites.RemoveAll(
                entry => string.Equals(entry, identifier, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
            {
                _state.Favourites.Add(identifier);
                isFavourite = true;
            }
            else
            {
                isFavourite = false;
            }
        }

        Save();

        return isFavourite;
    }

    /// <inheritdoc />
    public bool IsFavourite(string identifier)
    {
        lock (_gate)
        {
            return _state.Favourites.Exists(
                entry => string.Equals(entry, identifier, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <inheritdoc />
    public void Save()
    {
        try
        {
            string? directory = Path.GetDirectoryName(_path);

            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            string json;

            lock (_gate)
            {
                json = JsonSerializer.Serialize(_state, SerializerOptions);
            }

            File.WriteAllText(_path, json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "CRS preferences could not be saved to {Path}.", _path);
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path))
            {
                return;
            }

            PreferencesFile? loaded = JsonSerializer.Deserialize<PreferencesFile>(File.ReadAllText(_path));

            if (loaded is not null)
            {
                _state = loaded;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            _logger.LogWarning(ex, "CRS preferences could not be read from {Path}; starting empty.", _path);
        }
    }

    /// <summary>The on-disk shape. Mutable and public so <c>System.Text.Json</c> can populate it.</summary>
    private sealed class PreferencesFile
    {
        public List<string> Recent { get; set; } = [];

        public List<string> Favourites { get; set; } = [];

        public string? LastInput { get; set; }

        public string? LastOutput { get; set; }

        public string? ProjectDefault { get; set; }

        public string? CompanyDefault { get; set; }
    }
}
