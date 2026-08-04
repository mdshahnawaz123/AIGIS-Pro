using AiGisConverter.Domain.Common;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Abstractions;

/// <summary>
/// Constructive geometry operations.
/// </summary>
/// <remarks>
/// <para>
/// Results are returned as <see cref="Result{T}"/> rather than thrown. Overlay failures on
/// real survey data are common and expected &#8212; a sliver polygon with near-coincident edges
/// defeats the noding, and one such parcel must not abort a dissolve over a whole district.
/// </para>
/// <para>
/// Operations that reduce many geometries to one cannot stream by definition. Where the input set
/// is large this interface takes an enumerable and the implementation controls its own buffering,
/// rather than making the caller materialise a list it cannot bound.
/// </para>
/// </remarks>
public interface ISpatialOperations
{
    /// <summary>Expands or contracts a geometry by a distance.</summary>
    /// <param name="geometry">The geometry to buffer.</param>
    /// <param name="distance">The distance, in the geometry's own units. Negative shrinks.</param>
    /// <param name="parameters">Optional shape control.</param>
    /// <returns>The buffered geometry.</returns>
    Result<NtsGeometry> Buffer(NtsGeometry geometry, double distance, BufferParameters? parameters = null);

    /// <summary>Combines two geometries.</summary>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns>The union.</returns>
    Result<NtsGeometry> Union(NtsGeometry left, NtsGeometry right);

    /// <summary>Combines many geometries into one.</summary>
    /// <param name="geometries">The geometries to combine.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The union.</returns>
    Result<NtsGeometry> Union(IEnumerable<NtsGeometry> geometries, CancellationToken cancellationToken = default);

    /// <summary>Computes the geometry common to both inputs.</summary>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns>The intersection.</returns>
    Result<NtsGeometry> Intersection(NtsGeometry left, NtsGeometry right);

    /// <summary>Computes the part of the first geometry not covered by the second.</summary>
    /// <param name="subject">The geometry to subtract from.</param>
    /// <param name="subtrahend">The geometry to remove.</param>
    /// <returns>The difference.</returns>
    Result<NtsGeometry> Difference(NtsGeometry subject, NtsGeometry subtrahend);

    /// <summary>Computes the parts belonging to exactly one of the inputs.</summary>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns>The symmetric difference.</returns>
    Result<NtsGeometry> SymmetricDifference(NtsGeometry left, NtsGeometry right);

    /// <summary>
    /// Merges adjacent geometries that share a grouping key, dissolving the boundaries between them.
    /// </summary>
    /// <remarks>
    /// The operation behind "give me one polygon per land use rather than one per parcel". The
    /// grouping key is supplied by the caller because the domain has no opinion about what should
    /// be dissolved together.
    /// </remarks>
    /// <typeparam name="TSource">The element type.</typeparam>
    /// <param name="source">The elements to dissolve.</param>
    /// <param name="keySelector">Produces the grouping key.</param>
    /// <param name="geometrySelector">Produces the geometry.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>One geometry per distinct key.</returns>
    Result<IReadOnlyDictionary<string, NtsGeometry>> Dissolve<TSource>(
        IEnumerable<TSource> source,
        Func<TSource, string> keySelector,
        Func<TSource, NtsGeometry?> geometrySelector,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Collects geometries into a single multi-part geometry without merging them.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="Union(IEnumerable{NtsGeometry}, CancellationToken)"/>: merging
    /// preserves every input part exactly, whereas a union dissolves shared boundaries and can
    /// change the part count. Merging is cheap and lossless; union is neither.
    /// </remarks>
    /// <param name="geometries">The geometries to collect.</param>
    /// <returns>The collected geometry.</returns>
    Result<NtsGeometry> Merge(IEnumerable<NtsGeometry> geometries);

    /// <summary>
    /// Clips a stream of geometries to a boundary, lazily.
    /// </summary>
    /// <param name="geometries">The geometries to clip.</param>
    /// <param name="boundary">The clip boundary.</param>
    /// <param name="cancellationToken">Token used to cancel the enumeration.</param>
    /// <returns>The clipped geometries. Those falling wholly outside are omitted.</returns>
    IAsyncEnumerable<NtsGeometry> ClipAsync(
        IAsyncEnumerable<NtsGeometry> geometries,
        NtsGeometry boundary,
        CancellationToken cancellationToken = default);
}

/// <summary>Shape control for a buffer.</summary>
/// <param name="QuadrantSegments">Segments used to approximate a quarter circle.</param>
/// <param name="EndCap">How line ends are finished.</param>
/// <param name="JoinStyle">How corners are finished.</param>
/// <param name="MitreLimit">How far a mitred corner may extend, as a multiple of the distance.</param>
public sealed record BufferParameters(
    int QuadrantSegments = 8,
    BufferEndCap EndCap = BufferEndCap.Round,
    BufferJoin JoinStyle = BufferJoin.Round,
    double MitreLimit = 5d);

/// <summary>How a buffer finishes the end of a line.</summary>
public enum BufferEndCap
{
    /// <summary>Semicircular. The default, and what a pipe corridor wants.</summary>
    Round = 0,

    /// <summary>Squared off beyond the endpoint.</summary>
    Square = 1,

    /// <summary>Cut flat at the endpoint.</summary>
    Flat = 2,
}

/// <summary>How a buffer finishes a corner.</summary>
public enum BufferJoin
{
    /// <summary>Arc. The default.</summary>
    Round = 0,

    /// <summary>Extended to a point, limited by the mitre limit.</summary>
    Mitre = 1,

    /// <summary>Cut flat across the corner.</summary>
    Bevel = 2,
}
