using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Abstractions;

/// <summary>
/// Indexed queries over a feature set.
/// </summary>
/// <remarks>
/// Wraps <c>ISpatialIndex</c> rather than replacing it, and adds the queries the index has no
/// business knowing about: radius search, which needs a distance model, and predicate queries
/// expressed through <see cref="ITopologyEngine"/>. The index stays a pure R-tree.
/// </remarks>
public interface ISpatialQueryEngine
{
    /// <summary>Gets the number of indexed features.</summary>
    int Count { get; }

    /// <summary>Gets the combined extent of everything indexed.</summary>
    Extent Extent { get; }

    /// <summary>Loads features and builds the index.</summary>
    /// <param name="features">The features to index.</param>
    /// <param name="coordinateSystem">The system the coordinates are in, for distance queries.</param>
    /// <param name="cancellationToken">Token used to cancel the load.</param>
    void Load(
        IEnumerable<GisFeature> features,
        CoordinateSystem coordinateSystem,
        CancellationToken cancellationToken = default);

    /// <summary>Finds features whose bounding box overlaps an extent.</summary>
    /// <param name="extent">The search extent.</param>
    /// <returns>The candidates. Bounding-box accurate only.</returns>
    IReadOnlyList<GisFeature> QueryBoundingBox(Extent extent);

    /// <summary>
    /// Finds features within a distance of a point.
    /// </summary>
    /// <remarks>
    /// The radius is in metres for a geographic system and in the system's own linear units for a
    /// projected one, matching how <see cref="ISpatialAnalysis"/> reports distance. The candidate
    /// envelope is expanded by a degree equivalent when the system is geographic, because a metre
    /// radius cannot be applied to a degree envelope directly.
    /// </remarks>
    /// <param name="x">Centre X.</param>
    /// <param name="y">Centre Y.</param>
    /// <param name="radius">The search radius.</param>
    /// <returns>The features inside the radius, with their distances, closest first.</returns>
    IReadOnlyList<FeatureDistance> QueryRadius(double x, double y, double radius);

    /// <summary>Finds the nearest features to a geometry.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <param name="count">How many to return.</param>
    /// <returns>The nearest features with their distances, closest first.</returns>
    IReadOnlyList<FeatureDistance> QueryNearest(NtsGeometry geometry, int count = 1);

    /// <summary>Finds features satisfying a topological predicate against a geometry.</summary>
    /// <param name="geometry">The search geometry.</param>
    /// <param name="predicate">The predicate to apply.</param>
    /// <returns>The matching features.</returns>
    IReadOnlyList<GisFeature> Query(NtsGeometry geometry, SpatialPredicate predicate);
}

/// <summary>A feature and how far it is from the search subject.</summary>
/// <param name="Feature">The feature.</param>
/// <param name="Distance">The distance, in the units the query engine reports.</param>
public sealed record FeatureDistance(GisFeature Feature, Measurement Distance);

/// <summary>The topological predicates a query may filter on.</summary>
public enum SpatialPredicate
{
    /// <summary>Shares at least one point.</summary>
    Intersects = 0,

    /// <summary>Shares a boundary point but no interior point.</summary>
    Touches = 1,

    /// <summary>Lies entirely inside the search geometry.</summary>
    Within = 2,

    /// <summary>Entirely encloses the search geometry.</summary>
    Contains = 3,

    /// <summary>Shares interior points without containment, same dimension.</summary>
    Overlaps = 4,

    /// <summary>Intersects in something of lower dimension than both.</summary>
    Crosses = 5,

    /// <summary>Shares no point at all.</summary>
    Disjoint = 6,
}
