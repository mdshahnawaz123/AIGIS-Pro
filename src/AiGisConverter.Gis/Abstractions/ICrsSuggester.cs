using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Infers the coordinate system a drawing is most likely in, from its coordinates and metadata.
/// </summary>
/// <remarks>
/// Detection is honest about what the numbers can and cannot say. Longitude/latitude coordinates
/// identify WGS 84 with confidence; a declared CRS or a <c>.prj</c> sidecar is near-certain; but a
/// bare easting/northing pair is valid in every UTM zone at once, so the zone cannot be recovered
/// from the coordinates alone. In that case the suggester says so rather than guessing, which is
/// why every candidate carries a confidence and a reason.
/// </remarks>
public interface ICrsSuggester
{
    /// <summary>Ranks the coordinate systems a drawing might be in.</summary>
    /// <param name="sourceExtent">The drawing's extent in its own coordinates.</param>
    /// <param name="declaredCrs">A CRS declared by the drawing or a sidecar, or null.</param>
    /// <param name="cancellationToken">Token used to cancel the work.</param>
    /// <returns>Candidates ordered by confidence, highest first. May be empty.</returns>
    Task<IReadOnlyList<CrsSuggestion>> SuggestAsync(
        Extent sourceExtent,
        string? declaredCrs = null,
        CancellationToken cancellationToken = default);
}

/// <summary>One ranked coordinate-system candidate.</summary>
/// <param name="CoordinateSystem">The suggested system, or null for "local engineering coordinates".</param>
/// <param name="Confidence">Confidence between 0 and 1.</param>
/// <param name="Reason">A short human-readable justification.</param>
public sealed record CrsSuggestion(CoordinateSystem? CoordinateSystem, double Confidence, string Reason)
{
    /// <summary>The confidence below which detection is treated as inconclusive.</summary>
    public const double ConfidenceThreshold = 0.60d;

    /// <summary>Gets a value indicating whether this candidate is confident enough to apply.</summary>
    public bool IsConfident => Confidence >= ConfidenceThreshold;

    /// <summary>Gets the display label, for example <c>EPSG:32640 (95%)</c> or <c>Local coordinates</c>.</summary>
    public string Label =>
        (CoordinateSystem is null ? "Local engineering coordinates" : CoordinateSystem.Identifier)
        + $" ({Confidence:P0})";
}
