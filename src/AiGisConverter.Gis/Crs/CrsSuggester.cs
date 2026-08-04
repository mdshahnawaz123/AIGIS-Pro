using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;

namespace AiGisConverter.Gis.Crs;

/// <summary>
/// Default <see cref="ICrsSuggester"/>: classifies a drawing's coordinates and ranks candidates.
/// </summary>
/// <remarks>
/// <para>
/// The chain of signals, strongest first: a declared CRS or sidecar; longitude/latitude ranges that
/// only a geographic system fits; and easting/northing magnitudes typical of a UTM grid. The last
/// case is the hard one — the same easting/northing is valid in every UTM zone — so rather than
/// guess a single zone, the suggester transforms the coordinates under each plausible zone, names
/// the region that zone would place the drawing in, and returns the shortlist for the operator to
/// confirm. That is how a surveyor recognises the right answer ("that's the UAE, so zone 40N")
/// without the tool pretending to a certainty the numbers do not support.
/// </para>
/// </remarks>
public sealed class CrsSuggester : ICrsSuggester
{
    private const double MetresPerDegreeLatitude = 111_132d;
    private const double MetresPerDegreeLongitude = 111_320d;
    private const double SouthernHemisphereFalseNorthing = 10_000_000d;
    private const int Wgs84UtmNorthBase = 32_600;

    private readonly ICrsCatalog _catalog;

    /// <summary>Initializes a new instance of the <see cref="CrsSuggester"/> class.</summary>
    /// <param name="catalog">The EPSG/PROJ catalogue, for resolving a declared CRS and naming regions.</param>
    public CrsSuggester(ICrsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        _catalog = catalog;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<CrsSuggestion>> SuggestAsync(
        Extent sourceExtent,
        string? declaredCrs = null,
        CancellationToken cancellationToken = default)
    {
        List<CrsSuggestion> suggestions = [];

        // 1. A CRS the drawing itself declares (or a .prj sidecar) beats any inference.
        if (!string.IsNullOrWhiteSpace(declaredCrs))
        {
            CrsCatalogEntry? declared = await _catalog.FindAsync(declaredCrs, cancellationToken).ConfigureAwait(false);

            if (declared is not null)
            {
                suggestions.Add(new CrsSuggestion(
                    declared.CoordinateSystem, 0.98d, "Declared by the drawing or its .prj sidecar."));
            }
        }

        if (sourceExtent.IsEmpty)
        {
            return suggestions.Count > 0
                ? suggestions
                : [new CrsSuggestion(null, 0d, "The drawing has no geometry to analyse.")];
        }

        double centreX = sourceExtent.CentreX;
        double centreY = sourceExtent.CentreY;

        if (LooksGeographic(sourceExtent))
        {
            suggestions.Add(new CrsSuggestion(
                CoordinateSystem.Wgs84,
                0.90d,
                $"Coordinates are longitude/latitude (~{centreX:F2}°, {centreY:F2}°)."));
        }
        else if (LooksLikeUtm(centreX, centreY))
        {
            suggestions.AddRange(await RankUtmZonesAsync(centreX, centreY, cancellationToken).ConfigureAwait(false));

            suggestions.Add(new CrsSuggestion(
                null, 0.30d, "Or treat the drawing as a local engineering grid (no georeferencing)."));
        }
        else
        {
            suggestions.Add(new CrsSuggestion(
                null,
                0.55d,
                "Coordinates are not in any global range; the drawing is most likely in local "
                + "engineering coordinates and cannot be placed on a world map."));
        }

        return [.. suggestions.OrderByDescending(static s => s.Confidence)];
    }

    /// <summary>
    /// Ranks the UTM zones the coordinates could belong to, by the region each zone would imply.
    /// </summary>
    private async Task<IReadOnlyList<CrsSuggestion>> RankUtmZonesAsync(
        double easting,
        double northing,
        CancellationToken cancellationToken)
    {
        bool northern = northing < SouthernHemisphereFalseNorthing;
        double latitude = northern
            ? northing / MetresPerDegreeLatitude
            : (northing - SouthernHemisphereFalseNorthing) / MetresPerDegreeLatitude;

        double cosLat = Math.Max(0.2d, Math.Cos(latitude * Math.PI / 180d));
        double deltaLongitude = (easting - 500_000d) / (MetresPerDegreeLongitude * cosLat);

        List<(CrsSuggestion Suggestion, double AreaSize)> ranked = [];

        for (int zone = 1; zone <= 60; zone++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            double centralMeridian = -183d + (6d * zone);
            double longitude = centralMeridian + deltaLongitude;

            if (longitude is < -180d or > 180d)
            {
                continue;
            }

            // Name the region this zone would place the drawing in. A zone whose coordinates land
            // in a specific, named area of use is a plausible answer; one that lands nowhere is not.
            IReadOnlyList<CrsCatalogEntry> covering =
                await _catalog.FindByLocationAsync(longitude, latitude, 1, cancellationToken).ConfigureAwait(false);

            if (covering.Count == 0 || covering[0].AreaName is not { Length: > 0 } region)
            {
                continue;
            }

            int code = Wgs84UtmNorthBase + zone; // northern-hemisphere WGS 84 UTM
            CoordinateSystem system = CoordinateSystem.Create("EPSG", code, $"WGS 84 / UTM zone {zone}N", isGeographic: false);

            ranked.Add((
                new CrsSuggestion(
                    system,
                    // Honest: several zones are geometrically valid, so no single one is certain.
                    // The shortlist exists for the operator to recognise the right region.
                    0.55d,
                    $"If in zone {zone}N, the drawing sits in {region} (~{longitude:F1}°E, {latitude:F1}°N)."),
                covering[0].AreaSizeDegrees));
        }

        // Most specific (smallest) area of use first — the likeliest recognisable region.
        return [.. ranked.OrderBy(static r => r.AreaSize).Take(6).Select(static r => r.Suggestion)];
    }

    private static bool LooksGeographic(Extent extent) =>
        extent.MinX >= -180d && extent.MaxX <= 180d
        && extent.MinY >= -90d && extent.MaxY <= 90d
        && extent.Width <= 20d && extent.Height <= 20d;

    private static bool LooksLikeUtm(double easting, double northing) =>
        easting is > 100_000d and < 900_000d
        && northing is > -1_000_000d and < 10_100_000d;
}
