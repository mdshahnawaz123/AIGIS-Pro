using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Abstractions;

/// <summary>
/// The OGC binary topological predicates, evaluated between two geometries.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from the predicate queries on <c>ISpatialIndex</c>, which answer "which features in
/// this set satisfy the predicate". This answers "do these two geometries satisfy it", with no
/// index and no feature set involved, which is what an operation such as a clip or a dissolve
/// needs.
/// </para>
/// <para>
/// The predicates are mutually exclusive in ways that surprise people: touching is not
/// intersecting for the purpose of <see cref="Overlaps"/>, a geometry does not overlap one it
/// contains, and <see cref="Crosses"/> requires the intersection to have a lower dimension than
/// at least one input. The remarks on each member say what the OGC definition actually is, because
/// guessing produces queries that silently return the wrong rows.
/// </para>
/// </remarks>
public interface ITopologyEngine
{
    /// <summary>
    /// Determines whether the geometries share at least one point.
    /// </summary>
    /// <remarks>The weakest predicate: everything else here implies it, except disjoint.</remarks>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns><see langword="true"/> when the geometries are not disjoint.</returns>
    bool Intersects(NtsGeometry left, NtsGeometry right);

    /// <summary>
    /// Determines whether the geometries share a boundary point but no interior point.
    /// </summary>
    /// <remarks>
    /// Two adjacent parcels touch. A parcel and itself do not: a geometry never touches itself,
    /// because their interiors coincide.
    /// </remarks>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns><see langword="true"/> when the geometries touch.</returns>
    bool Touches(NtsGeometry left, NtsGeometry right);

    /// <summary>Determines whether the first geometry lies entirely inside the second.</summary>
    /// <param name="inner">The geometry that may be contained.</param>
    /// <param name="outer">The geometry that may contain it.</param>
    /// <returns><see langword="true"/> when <paramref name="inner"/> is within <paramref name="outer"/>.</returns>
    bool Within(NtsGeometry inner, NtsGeometry outer);

    /// <summary>Determines whether the first geometry entirely encloses the second.</summary>
    /// <remarks>The converse of <see cref="Within"/>: <c>a.Contains(b) == b.Within(a)</c>.</remarks>
    /// <param name="outer">The geometry that may contain.</param>
    /// <param name="inner">The geometry that may be contained.</param>
    /// <returns><see langword="true"/> when <paramref name="outer"/> contains <paramref name="inner"/>.</returns>
    bool Contains(NtsGeometry outer, NtsGeometry inner);

    /// <summary>
    /// Determines whether the geometries share interior points without either containing the other.
    /// </summary>
    /// <remarks>
    /// Requires both geometries to have the same dimension. A line never overlaps a polygon; it
    /// crosses it.
    /// </remarks>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns><see langword="true"/> when the geometries overlap partially.</returns>
    bool Overlaps(NtsGeometry left, NtsGeometry right);

    /// <summary>
    /// Determines whether the geometries intersect in something of lower dimension than both.
    /// </summary>
    /// <remarks>
    /// The predicate for a pipe crossing a parcel boundary, or two roads meeting at a junction.
    /// This is the one most often confused with <see cref="Overlaps"/>, and the confusion produces
    /// a query that returns nothing with no error.
    /// </remarks>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns><see langword="true"/> when the geometries cross.</returns>
    bool Crosses(NtsGeometry left, NtsGeometry right);

    /// <summary>Determines whether the geometries share no point at all.</summary>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns><see langword="true"/> when the geometries are disjoint.</returns>
    bool Disjoint(NtsGeometry left, NtsGeometry right);

    /// <summary>
    /// Computes the full DE-9IM intersection matrix.
    /// </summary>
    /// <remarks>
    /// The named predicates are shorthands for particular patterns in this matrix. Exposing it
    /// lets a caller express a relationship the OGC set has no name for, without this interface
    /// growing a method per question.
    /// </remarks>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <returns>The nine-character matrix, for example <c>212101212</c>.</returns>
    string Relate(NtsGeometry left, NtsGeometry right);

    /// <summary>Tests a geometry pair against a DE-9IM pattern.</summary>
    /// <param name="left">First geometry.</param>
    /// <param name="right">Second geometry.</param>
    /// <param name="pattern">The nine-character pattern, which may contain <c>*</c>, <c>T</c> and <c>F</c>.</param>
    /// <returns><see langword="true"/> when the relationship matches the pattern.</returns>
    bool Relate(NtsGeometry left, NtsGeometry right, string pattern);
}
