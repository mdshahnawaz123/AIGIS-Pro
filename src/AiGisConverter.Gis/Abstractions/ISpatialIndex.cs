using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// An R-tree over a feature set, supporting the standard spatial predicates.
/// </summary>
/// <remarks>
/// <para>
/// Every query is two-phase: the tree narrows by bounding box, then the exact predicate runs on
/// the survivors. That distinction matters for correctness as well as speed &#8212; a bounding-box
/// hit is not an intersection, and treating it as one is the classic spatial-query bug.
/// </para>
/// <para>
/// Building an index is opt-in. A straight streaming export never needs one, and paying to build a
/// tree over a million features that nothing will query is pure waste.
/// </para>
/// </remarks>
public interface ISpatialIndex
{
    /// <summary>Gets the number of indexed features.</summary>
    int Count { get; }

    /// <summary>Gets the combined extent of everything indexed.</summary>
    Extent Extent { get; }

    /// <summary>Adds a feature. Must be called before the first query.</summary>
    /// <param name="feature">The feature to index.</param>
    void Insert(GisFeature feature);

    /// <summary>Builds the tree. Called automatically by the first query.</summary>
    void Build();

    /// <summary>Finds features whose bounding box overlaps the given extent.</summary>
    /// <param name="extent">The search extent.</param>
    /// <returns>The candidates. Bounding-box accurate only.</returns>
    IReadOnlyList<GisFeature> QueryBoundingBox(Extent extent);

    /// <summary>Finds features that genuinely intersect a geometry.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <returns>The matching features.</returns>
    IReadOnlyList<GisFeature> QueryIntersects(NtsGeometry geometry);

    /// <summary>Finds features that contain a geometry.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <returns>The matching features.</returns>
    IReadOnlyList<GisFeature> QueryContains(NtsGeometry geometry);

    /// <summary>Finds features wholly within a geometry.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <returns>The matching features.</returns>
    IReadOnlyList<GisFeature> QueryWithin(NtsGeometry geometry);

    /// <summary>Finds features that touch a geometry without overlapping it.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <returns>The matching features.</returns>
    IReadOnlyList<GisFeature> QueryTouches(NtsGeometry geometry);

    /// <summary>Finds features that overlap a geometry partially.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <returns>The matching features.</returns>
    IReadOnlyList<GisFeature> QueryOverlaps(NtsGeometry geometry);

    /// <summary>Finds the nearest features to a geometry.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <param name="count">How many to return.</param>
    /// <returns>The nearest features, closest first.</returns>
    IReadOnlyList<GisFeature> QueryNearest(NtsGeometry geometry, int count = 1);
}
