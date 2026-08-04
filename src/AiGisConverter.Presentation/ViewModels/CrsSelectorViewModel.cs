using System.Collections.ObjectModel;
using AiGisConverter.Gis.Abstractions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiGisConverter.Presentation.ViewModels;

/// <summary>
/// A searchable coordinate-system picker, backed by the full EPSG/PROJ catalogue.
/// </summary>
/// <remarks>
/// <para>
/// One instance drives one field (Input CRS or Output CRS). It holds the search text, the current
/// results, and the chosen system, so the same control can be placed twice without duplicating
/// logic. Search runs asynchronously against <see cref="ICrsCatalog"/>; the UI thread is never
/// blocked while proj.db is queried.
/// </para>
/// <para>
/// The chosen system is exposed as an identifier string (<c>EPSG:32640</c>) so the existing
/// project-building code, which parses an identifier, is unaffected.
/// </para>
/// </remarks>
public sealed partial class CrsSelectorViewModel : ObservableObject, IDisposable
{
    private readonly ICrsCatalog _catalog;
    private readonly ICrsPreferences _preferences;
    private CancellationTokenSource? _pending;
    private bool _disposed;

    [ObservableProperty]
    private string _label;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private CrsCatalogEntry? _selected;

    [ObservableProperty]
    private string _selectedIdentifier;

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private string _detectionMessage = string.Empty;

    [ObservableProperty]
    private CrsInfo? _selectedInfo;

    [ObservableProperty]
    private bool _hasInfo;

    [ObservableProperty]
    private bool _isFavourite;

    /// <summary>Initializes a new instance of the <see cref="CrsSelectorViewModel"/> class.</summary>
    /// <param name="catalog">The EPSG/PROJ catalogue.</param>
    /// <param name="preferences">Remembers favourite and recently used systems.</param>
    /// <param name="label">The field label, for example <c>Output coordinate system</c>.</param>
    /// <param name="initialIdentifier">The identifier to start on, for example <c>EPSG:4326</c>.</param>
    public CrsSelectorViewModel(ICrsCatalog catalog, ICrsPreferences preferences, string label, string initialIdentifier)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        ArgumentNullException.ThrowIfNull(preferences);
        _catalog = catalog;
        _preferences = preferences;
        _label = label;
        _selectedIdentifier = initialIdentifier;

        // Warm the whole catalogue in the background so the first search is instant.
        _ = _catalog.PreloadAsync();
        _ = LoadQuickPicksAsync();
    }

    /// <summary>Gets the operator's favourite systems, for one-click selection.</summary>
    public ObservableCollection<CrsCatalogEntry> Favourites { get; } = [];

    /// <summary>Gets the recently used systems, newest first.</summary>
    public ObservableCollection<CrsCatalogEntry> Recent { get; } = [];

    /// <summary>Loads the favourite and recent lists from the persisted preferences.</summary>
    private async Task LoadQuickPicksAsync()
    {
        await Fill(Favourites, _preferences.Favourites).ConfigureAwait(true);
        await Fill(Recent, _preferences.Recent).ConfigureAwait(true);

        async Task Fill(ObservableCollection<CrsCatalogEntry> target, IReadOnlyList<string> identifiers)
        {
            target.Clear();

            foreach (string identifier in identifiers)
            {
                CrsCatalogEntry? entry = await _catalog.FindAsync(identifier).ConfigureAwait(true);

                if (entry is not null)
                {
                    target.Add(entry);
                }
            }
        }
    }

    /// <summary>Records the current selection as used and refreshes the quick-pick lists.</summary>
    public void RememberSelection()
    {
        if (CoordinateSystemIdentifierIsUsable(SelectedIdentifier))
        {
            _preferences.RecordUse(SelectedIdentifier);
            _ = LoadQuickPicksAsync();
        }
    }

    /// <summary>Adds or removes the current selection from favourites.</summary>
    [RelayCommand]
    private void ToggleFavourite()
    {
        if (!CoordinateSystemIdentifierIsUsable(SelectedIdentifier))
        {
            return;
        }

        IsFavourite = _preferences.ToggleFavourite(SelectedIdentifier);
        _ = LoadQuickPicksAsync();
    }

    private static bool CoordinateSystemIdentifierIsUsable(string identifier) =>
        !string.IsNullOrWhiteSpace(identifier)
        && Domain.ValueObjects.CoordinateSystem.TryParse(identifier, out _);

    /// <summary>Gets the current search results.</summary>
    public ObservableCollection<CrsCatalogEntry> Results { get; } = [];

    /// <summary>Gets a value indicating whether the catalogue database is available.</summary>
    public bool IsCatalogAvailable => _catalog.IsAvailable;

    /// <summary>Runs a search when the query changes, debounced so keystrokes do not stack.</summary>
    partial void OnSearchTextChanged(string value) => _ = SearchAsync(value);

    /// <summary>Adopts the selected entry's identifier when the user picks a result.</summary>
    partial void OnSelectedChanged(CrsCatalogEntry? value)
    {
        if (value is not null)
        {
            SelectedIdentifier = value.CoordinateSystem.Identifier;
        }
    }

    /// <summary>Loads the information panel whenever the chosen identifier changes.</summary>
    partial void OnSelectedIdentifierChanged(string value) => _ = LoadInfoAsync(value);

    private async Task LoadInfoAsync(string identifier)
    {
        CrsCatalogEntry? entry = await _catalog.FindAsync(identifier).ConfigureAwait(true);

        if (entry is null)
        {
            SelectedInfo = null;
            HasInfo = false;
            return;
        }

        (string zone, string centralMeridian) = UtmDetails(entry.CoordinateSystem.Code);

        SelectedInfo = new CrsInfo(
            entry.Identifier,
            entry.DisplayName,
            entry.IsProjected ? "Projected" : "Geographic",
            entry.Datum ?? "—",
            entry.IsProjected ? entry.ProjectionMethod ?? "—" : "—",
            entry.Units ?? "—",
            entry.IsProjected ? "Easting, Northing" : "Latitude, Longitude",
            entry.AreaName ?? "—",
            zone,
            centralMeridian);

        HasInfo = true;
        IsFavourite = _preferences.IsFavourite(identifier);
    }

    /// <summary>Derives the UTM zone and central meridian from a WGS 84 UTM EPSG code.</summary>
    private static (string Zone, string CentralMeridian) UtmDetails(int code)
    {
        int? zone = code switch
        {
            >= 32601 and <= 32660 => code - 32600,
            >= 32701 and <= 32760 => code - 32700,
            _ => null,
        };

        if (zone is null)
        {
            return ("—", "—");
        }

        string hemisphere = code >= 32701 ? "S" : "N";
        int meridian = -183 + (6 * zone.Value);

        return ($"{zone.Value}{hemisphere}", $"{meridian}°");
    }

    private async Task SearchAsync(string query)
    {
        // Cancel and dispose an in-flight search: only the latest keystroke's results should land.
        CancellationTokenSource? previous = _pending;
        _pending = new CancellationTokenSource();
        previous?.Cancel();
        previous?.Dispose();
        CancellationToken token = _pending.Token;

        if (string.IsNullOrWhiteSpace(query))
        {
            Results.Clear();
            return;
        }

        try
        {
            IsSearching = true;
            await Task.Delay(150, token).ConfigureAwait(true); // debounce
            IReadOnlyList<CrsCatalogEntry> found = await _catalog.SearchAsync(query, 50, token).ConfigureAwait(true);

            if (token.IsCancellationRequested)
            {
                return;
            }

            Results.Clear();

            foreach (CrsCatalogEntry entry in found)
            {
                Results.Add(entry);
            }
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer keystroke; nothing to do.
        }
        finally
        {
            if (!token.IsCancellationRequested)
            {
                IsSearching = false;
            }
        }
    }

    /// <summary>
    /// Shows a set of detected candidate systems as results, for the operator to pick from.
    /// </summary>
    /// <param name="identifiers">The candidate identifiers, best first.</param>
    public async Task ShowSuggestedAsync(IReadOnlyList<string> identifiers)
    {
        ArgumentNullException.ThrowIfNull(identifiers);

        List<CrsCatalogEntry> resolved = [];

        foreach (string identifier in identifiers)
        {
            CrsCatalogEntry? entry = await _catalog.FindAsync(identifier).ConfigureAwait(true);

            if (entry is not null)
            {
                resolved.Add(entry);
            }
        }

        Results.Clear();

        foreach (CrsCatalogEntry entry in resolved)
        {
            Results.Add(entry);
        }
    }

    /// <summary>Clears the current search text and results.</summary>
    [RelayCommand]
    private void ClearSearch()
    {
        SearchText = string.Empty;
        Results.Clear();
    }

    /// <summary>Cancels any in-flight search and releases the cancellation source.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _pending?.Cancel();
        _pending?.Dispose();
        _pending = null;
    }
}

/// <summary>The human-readable details of a coordinate system, for the information panel.</summary>
/// <param name="Epsg">The EPSG identifier, for example <c>EPSG:32640</c>.</param>
/// <param name="Name">The full name, for example <c>WGS 84 / UTM zone 40N</c>.</param>
/// <param name="Type">Either <c>Projected</c> or <c>Geographic</c>.</param>
/// <param name="Datum">The datum name.</param>
/// <param name="Projection">The projection method, or <c>—</c> for a geographic system.</param>
/// <param name="Units">The axis unit, for example <c>metre</c>.</param>
/// <param name="AxisOrder">The axis order, for example <c>Easting, Northing</c>.</param>
/// <param name="AreaOfUse">The area of use.</param>
/// <param name="UtmZone">The UTM zone, or <c>—</c> when not a UTM system.</param>
/// <param name="CentralMeridian">The central meridian, or <c>—</c> when not applicable.</param>
public sealed record CrsInfo(
    string Epsg,
    string Name,
    string Type,
    string Datum,
    string Projection,
    string Units,
    string AxisOrder,
    string AreaOfUse,
    string UtmZone,
    string CentralMeridian);
