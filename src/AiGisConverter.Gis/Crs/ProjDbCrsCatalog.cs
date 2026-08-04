using System.Globalization;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Gis.Crs;

/// <summary>
/// <see cref="ICrsCatalog"/> backed by PROJ's <c>proj.db</c>, loaded once into memory.
/// </summary>
/// <remarks>
/// <para>
/// The whole catalogue — roughly ten thousand systems, with each system's datum, projection method,
/// units and area of use — is read once, on the first request, and then every search runs against
/// the in-memory copy. Querying the database on every keystroke was both slower and less reliable
/// (a locked or briefly-missing file produced an empty result and a search box that looked broken);
/// loading it once removes that whole class of failure.
/// </para>
/// <para>
/// Area-of-use names in <c>proj.db</c> use their own phrasing ("UAE - Dubai municipality"), so a
/// small alias table lets common country names match what the database actually stores.
/// </para>
/// </remarks>
public sealed class ProjDbCrsCatalog : ICrsCatalog, IDisposable
{
    private static readonly IReadOnlyDictionary<string, string> AreaAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["united arab emirates"] = "uae",
            ["emirates"] = "uae",
            ["usa"] = "united states",
            ["america"] = "united states",
            ["uk"] = "united kingdom",
            ["britain"] = "united kingdom",
            ["korea"] = "republic of korea",
            ["russia"] = "russian federation",
        };

    private readonly ProjDbLocator _locator;
    private readonly ILogger<ProjDbCrsCatalog> _logger;
    private readonly SemaphoreSlim _loadGate = new(1, 1);

    private IReadOnlyList<IndexedEntry>? _all;

    /// <summary>Initializes a new instance of the <see cref="ProjDbCrsCatalog"/> class.</summary>
    /// <param name="locator">Locates <c>proj.db</c> on disk.</param>
    /// <param name="logger">Logger for the catalogue.</param>
    public ProjDbCrsCatalog(ProjDbLocator locator, ILogger<ProjDbCrsCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(locator);
        ArgumentNullException.ThrowIfNull(logger);
        _locator = locator;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool IsAvailable => _locator.Path is not null;

    /// <inheritdoc />
    public async Task PreloadAsync(CancellationToken cancellationToken = default) =>
        await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrsCatalogEntry>> SearchAsync(
        string query,
        int maxResults = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        string trimmed = query.Trim();

        if (trimmed.Length == 0)
        {
            return [];
        }

        IReadOnlyList<IndexedEntry> all = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        string lowered = trimmed.ToLowerInvariant();
        string[] tokens = Tokenise(lowered);

        List<(CrsCatalogEntry Entry, int Score)> matches = [];

        foreach (IndexedEntry indexed in all)
        {
            bool exactCode = indexed.Code == lowered;
            bool allTokens = exactCode || tokens.All(token => indexed.Haystack.Contains(token, StringComparison.Ordinal));

            if (!allTokens)
            {
                continue;
            }

            int score = exactCode ? 0
                : indexed.Entry.DisplayName.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ? 1
                : 2;

            matches.Add((indexed.Entry, score));
        }

        return [.. matches
            .OrderBy(static m => m.Score)
            .ThenBy(static m => m.Entry.DisplayName.Length)
            .Take(maxResults)
            .Select(static m => m.Entry)];
    }

    /// <inheritdoc />
    public async Task<CrsCatalogEntry?> FindAsync(string identifier, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identifier);

        if (!CoordinateSystem.TryParse(identifier, out CoordinateSystem? parsed) || parsed is null)
        {
            return null;
        }

        IReadOnlyList<IndexedEntry> all = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        return all.FirstOrDefault(indexed =>
                indexed.Entry.CoordinateSystem.Code == parsed.Code
                && string.Equals(indexed.Entry.CoordinateSystem.Authority, parsed.Authority, StringComparison.OrdinalIgnoreCase))
            ?.Entry;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrsCatalogEntry>> FindByLocationAsync(
        double longitude,
        double latitude,
        int maxResults = 20,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<IndexedEntry> all = await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);

        return [.. all
            .Select(static indexed => indexed.Entry)
            .Where(entry => entry.IsProjected && entry.AreaContains(longitude, latitude))
            .OrderBy(static entry => entry.AreaSizeDegrees)
            .Take(maxResults)];
    }

    private async Task<IReadOnlyList<IndexedEntry>> EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_all is not null)
        {
            return _all;
        }

        await _loadGate.WaitAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            return _all ??= await Task.Run(LoadAll, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _loadGate.Release();
        }
    }

    private IReadOnlyList<IndexedEntry> LoadAll()
    {
        if (_locator.Path is null)
        {
            _logger.LogWarning("proj.db was not found; the CRS catalogue is empty.");
            return [];
        }

        List<IndexedEntry> entries = [];

        try
        {
            SqliteConnectionStringBuilder connectionString = new()
            {
                DataSource = _locator.Path,
                Mode = SqliteOpenMode.ReadOnly,
            };

            using SqliteConnection connection = new(connectionString.ToString());
            connection.Open();

            Read(connection, ProjectedQuery, isProjected: true, entries);
            Read(connection, GeodeticQuery, isProjected: false, entries);

            _logger.LogInformation("Loaded {Count} coordinate systems from the PROJ catalogue.", entries.Count);
        }
        catch (SqliteException ex)
        {
            _logger.LogWarning(ex, "The PROJ catalogue could not be loaded from {Path}.", _locator.Path);
        }

        return entries;
    }

    private static void Read(SqliteConnection connection, string sql, bool isProjected, List<IndexedEntry> into)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;

        using SqliteDataReader reader = command.ExecuteReader();

        while (reader.Read())
        {
            int code = int.Parse(reader.GetString(0), CultureInfo.InvariantCulture);
            string name = reader.GetString(1);
            string kind = reader.IsDBNull(2) ? (isProjected ? "projected" : "geographic") : reader.GetString(2);
            string? datum = reader.IsDBNull(3) ? null : reader.GetString(3);
            string? projection = reader.IsDBNull(4) ? null : reader.GetString(4);
            string? units = reader.IsDBNull(5) ? null : reader.GetString(5);
            string? area = reader.IsDBNull(10) ? null : reader.GetString(10);

            bool isGeographic = kind.Contains("geographic", StringComparison.OrdinalIgnoreCase);

            CrsCatalogEntry entry = new(
                CoordinateSystem.Create("EPSG", code, name, isGeographic),
                isProjected ? "projected" : kind,
                area,
                datum,
                isProjected ? projection : null,
                units,
                reader.IsDBNull(6) ? null : reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetDouble(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8),
                reader.IsDBNull(9) ? null : reader.GetDouble(9));

            into.Add(new IndexedEntry(entry, code.ToString(CultureInfo.InvariantCulture), BuildHaystack(entry)));
        }
    }

    private static string BuildHaystack(CrsCatalogEntry entry)
    {
        string combined = string.Join(
            ' ',
            entry.CoordinateSystem.Code,
            entry.DisplayName,
            entry.AreaName,
            entry.Datum,
            entry.ProjectionMethod,
            entry.Units).ToLowerInvariant();

        // Fold country aliases into the searchable text so "United Arab Emirates" finds "UAE".
        foreach (KeyValuePair<string, string> alias in AreaAliases)
        {
            if (combined.Contains(alias.Value, StringComparison.Ordinal))
            {
                combined += " " + alias.Key;
            }
        }

        return combined;
    }

    private static string[] Tokenise(string lowered) =>
        lowered.Split([' ', ',', '/', '-'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Releases the load gate. The catalogue is a singleton, so this runs at shutdown.</summary>
    public void Dispose() => _loadGate.Dispose();

    /// <summary>A catalogue entry with a precomputed lowercase search string.</summary>
    private sealed record IndexedEntry(CrsCatalogEntry Entry, string Code, string Haystack);

    private const string ProjectedQuery =
        """
        SELECT p.code, p.name, 'projected', gd.name, cv.method_name, u.name,
               MIN(e.south_lat), MAX(e.north_lat), MIN(e.west_lon), MAX(e.east_lon), ex.name
        FROM projected_crs p
        LEFT JOIN geodetic_crs g   ON g.auth_name = p.geodetic_crs_auth_name AND g.code = p.geodetic_crs_code
        LEFT JOIN geodetic_datum gd ON gd.auth_name = g.datum_auth_name AND gd.code = g.datum_code
        LEFT JOIN conversion cv    ON cv.auth_name = p.conversion_auth_name AND cv.code = p.conversion_code
        LEFT JOIN axis ax          ON ax.coordinate_system_auth_name = p.coordinate_system_auth_name
                                   AND ax.coordinate_system_code = p.coordinate_system_code
                                   AND ax.coordinate_system_order = 1
        LEFT JOIN unit_of_measure u ON u.auth_name = ax.uom_auth_name AND u.code = ax.uom_code
        LEFT JOIN usage us          ON us.object_auth_name = p.auth_name AND us.object_code = p.code
        LEFT JOIN extent e          ON e.auth_name = us.extent_auth_name AND e.code = us.extent_code
        LEFT JOIN extent ex         ON ex.auth_name = us.extent_auth_name AND ex.code = us.extent_code
        WHERE p.auth_name = 'EPSG' AND p.deprecated = 0
        GROUP BY p.code
        """;

    private const string GeodeticQuery =
        """
        SELECT g.code, g.name, g.type, gd.name, NULL, u.name,
               MIN(e.south_lat), MAX(e.north_lat), MIN(e.west_lon), MAX(e.east_lon), ex.name
        FROM geodetic_crs g
        LEFT JOIN geodetic_datum gd ON gd.auth_name = g.datum_auth_name AND gd.code = g.datum_code
        LEFT JOIN axis ax          ON ax.coordinate_system_auth_name = g.coordinate_system_auth_name
                                   AND ax.coordinate_system_code = g.coordinate_system_code
                                   AND ax.coordinate_system_order = 1
        LEFT JOIN unit_of_measure u ON u.auth_name = ax.uom_auth_name AND u.code = ax.uom_code
        LEFT JOIN usage us          ON us.object_auth_name = g.auth_name AND us.object_code = g.code
        LEFT JOIN extent e          ON e.auth_name = us.extent_auth_name AND e.code = us.extent_code
        LEFT JOIN extent ex         ON ex.auth_name = us.extent_auth_name AND ex.code = us.extent_code
        WHERE g.auth_name = 'EPSG' AND g.deprecated = 0
        GROUP BY g.code
        """;
}
