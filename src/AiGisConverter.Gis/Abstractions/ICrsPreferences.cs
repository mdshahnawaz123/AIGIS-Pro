namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Remembers which coordinate systems this operator actually uses.
/// </summary>
/// <remarks>
/// <para>
/// A survey office works in a handful of systems and picks from twelve thousand. Recording the
/// recent and favourite ones turns the common case from a search into a click, which is the single
/// biggest usability difference between this and a raw EPSG list.
/// </para>
/// <para>
/// Persisted under <c>%LOCALAPPDATA%\AiGisConverter</c>, the same place the application already
/// keeps its logs and per-user settings. Failure to read or write is never fatal: the selectors
/// simply fall back to an empty history.
/// </para>
/// </remarks>
public interface ICrsPreferences
{
    /// <summary>Gets the most recently used systems, newest first (at most twenty).</summary>
    IReadOnlyList<string> Recent { get; }

    /// <summary>Gets the systems marked as favourites.</summary>
    IReadOnlyList<string> Favourites { get; }

    /// <summary>Gets or sets the identifier last used for the input coordinate system.</summary>
    string? LastInput { get; set; }

    /// <summary>Gets or sets the identifier last used for the output coordinate system.</summary>
    string? LastOutput { get; set; }

    /// <summary>Gets or sets the project-level default output system, when one is configured.</summary>
    string? ProjectDefault { get; set; }

    /// <summary>Gets or sets the organisation-wide default output system, when one is configured.</summary>
    string? CompanyDefault { get; set; }

    /// <summary>Records a system as used, moving it to the top of the recent list.</summary>
    /// <param name="identifier">The identifier, for example <c>EPSG:32640</c>.</param>
    void RecordUse(string identifier);

    /// <summary>Adds or removes a favourite.</summary>
    /// <param name="identifier">The identifier to toggle.</param>
    /// <returns><see langword="true"/> when the system is a favourite afterwards.</returns>
    bool ToggleFavourite(string identifier);

    /// <summary>Determines whether a system is a favourite.</summary>
    /// <param name="identifier">The identifier to test.</param>
    /// <returns><see langword="true"/> when it is a favourite.</returns>
    bool IsFavourite(string identifier);

    /// <summary>Writes the current state to disk.</summary>
    void Save();
}
