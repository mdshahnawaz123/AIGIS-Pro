using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Searches the full EPSG/PROJ catalogue and exposes each system's area of use.
/// </summary>
/// <remarks>
/// <para>
/// Backed by PROJ's own <c>proj.db</c> (roughly twelve thousand systems), not a table in this
/// codebase. <see cref="ICrsRegistry"/> resolves a known identifier into a definition;
/// <see cref="ICrsCatalog"/> answers the opposite question — "which systems match what the user
/// typed, or the place this drawing sits?" — which is what a searchable selector and confidence
/// ranking need.
/// </para>
/// <para>
/// All members are safe to call before the native GDAL stack is initialised: the catalogue reads
/// the database file directly and does not depend on the OSR bindings.
/// </para>
/// </remarks>
public interface ICrsCatalog
{
    /// <summary>Gets a value indicating whether the catalogue database was located and opened.</summary>
    bool IsAvailable { get; }

    /// <summary>Loads the whole catalogue into memory, if it has not been loaded already.</summary>
    /// <remarks>
    /// Called once at startup so the first search is instant. All other members load on demand, so
    /// calling this is an optimisation, never a precondition.
    /// </remarks>
    /// <param name="cancellationToken">Token used to cancel the load.</param>
    /// <returns>A task that completes when the catalogue is in memory.</returns>
    Task PreloadAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches by EPSG code, CRS name, area of use (country/city/region), UTM zone, datum or
    /// projection type.
    /// </summary>
    /// <param name="query">The free-text query. An exact numeric code is ranked first.</param>
    /// <param name="maxResults">The most rows to return.</param>
    /// <param name="cancellationToken">Token used to cancel the search.</param>
    /// <returns>Matching systems, most relevant first. Empty when nothing matches or the database is unavailable.</returns>
    Task<IReadOnlyList<CrsCatalogEntry>> SearchAsync(
        string query,
        int maxResults = 50,
        CancellationToken cancellationToken = default);

    /// <summary>Looks up a single system and its area of use by identifier.</summary>
    /// <param name="identifier">An identifier such as <c>EPSG:32640</c>.</param>
    /// <param name="cancellationToken">Token used to cancel the lookup.</param>
    /// <returns>The entry, or null when it is not in the catalogue.</returns>
    Task<CrsCatalogEntry?> FindAsync(string identifier, CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns every projected system whose area of use contains the given geographic point.
    /// </summary>
    /// <remarks>Used by auto-detection to rank candidates for a projected drawing by location.</remarks>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <param name="maxResults">The most rows to return.</param>
    /// <param name="cancellationToken">Token used to cancel the query.</param>
    /// <returns>Candidate systems whose area covers the point, smallest (tightest) area first.</returns>
    Task<IReadOnlyList<CrsCatalogEntry>> FindByLocationAsync(
        double longitude,
        double latitude,
        int maxResults = 20,
        CancellationToken cancellationToken = default);
}

/// <summary>One system in the EPSG/PROJ catalogue, with its area of use.</summary>
/// <param name="CoordinateSystem">The resolved system (authority, code, name, geographic flag).</param>
/// <param name="Kind">The PROJ type, for example <c>projected</c> or <c>geographic 2D</c>.</param>
/// <param name="AreaName">The human-readable area of use, for example <c>UAE - Dubai municipality</c>.</param>
/// <param name="Datum">The datum name, for example <c>World Geodetic System 1984 ensemble</c>.</param>
/// <param name="ProjectionMethod">The projection method for a projected system, for example <c>Transverse Mercator</c>; null for geographic systems.</param>
/// <param name="Units">The axis unit, for example <c>metre</c> or <c>degree</c>.</param>
/// <param name="SouthLatitude">South edge of the area of use, in degrees, or null when unknown.</param>
/// <param name="NorthLatitude">North edge of the area of use, in degrees, or null when unknown.</param>
/// <param name="WestLongitude">West edge of the area of use, in degrees, or null when unknown.</param>
/// <param name="EastLongitude">East edge of the area of use, in degrees, or null when unknown.</param>
public sealed record CrsCatalogEntry(
    CoordinateSystem CoordinateSystem,
    string Kind,
    string? AreaName,
    string? Datum,
    string? ProjectionMethod,
    string? Units,
    double? SouthLatitude,
    double? NorthLatitude,
    double? WestLongitude,
    double? EastLongitude)
{
    /// <summary>Gets a value indicating whether this is a projected system.</summary>
    public bool IsProjected => Kind.Contains("projected", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the EPSG-style identifier, for example <c>EPSG:32640</c>.</summary>
    public string Identifier => CoordinateSystem.Identifier;

    /// <summary>Gets the display name, for example <c>WGS 84 / UTM zone 40N</c>.</summary>
    public string DisplayName => CoordinateSystem.Name ?? CoordinateSystem.Identifier;
    /// <summary>Gets a value indicating whether the area of use is fully specified.</summary>
    public bool HasArea =>
        SouthLatitude.HasValue && NorthLatitude.HasValue
        && WestLongitude.HasValue && EastLongitude.HasValue;

    /// <summary>Gets the area of use in square degrees, or <see cref="double.MaxValue"/> when unknown.</summary>
    /// <remarks>A smaller area is a more specific, and usually more useful, match for a location.</remarks>
    public double AreaSizeDegrees =>
        HasArea
            ? Math.Max(0d, NorthLatitude!.Value - SouthLatitude!.Value)
              * Math.Max(0d, EastLongitude!.Value - WestLongitude!.Value)
            : double.MaxValue;

    /// <summary>Determines whether the area of use contains a geographic point.</summary>
    /// <param name="longitude">Longitude in degrees.</param>
    /// <param name="latitude">Latitude in degrees.</param>
    /// <returns><see langword="true"/> when the point falls within the area of use.</returns>
    public bool AreaContains(double longitude, double latitude) =>
        HasArea
        && latitude >= SouthLatitude!.Value && latitude <= NorthLatitude!.Value
        && longitude >= WestLongitude!.Value && longitude <= EastLongitude!.Value;
}
