using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Indexing;

/// <summary>
/// R-tree over a feature set, built on the Sort-Tile-Recursive packing in NetTopologySuite.
/// </summary>
/// <remarks>
/// <para>
/// Every predicate query runs in two phases. The tree narrows candidates by bounding box, then the
/// exact predicate runs on the survivors. Skipping the second phase is the classic spatial bug: a
/// bounding-box hit is not an intersection, and for diagonal linework the false-positive rate is
/// enormous.
/// </para>
/// <para>
/// The tree is bulk-loaded and immutable once built, which is what STR packing requires. Inserting
/// after the first query throws rather than silently returning stale results.
/// </para>
/// </remarks>
public sealed class RTreeSpatialIndex : ISpatialIndex
{
    private readonly STRtree<GisFeature> _tree = new();
    private readonly List<GisFeature> _features = [];

    private Extent _extent = Extent.Empty;
    private bool _built;

    /// <inheritdoc />
    public int Count => _features.Count;

    /// <inheritdoc />
    public Extent Extent => _extent;

    /// <inheritdoc />
    public void Insert(GisFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);

        if (_built)
        {
            throw new InvalidOperationException(
                "The index has been built and cannot accept more features. Build a new index instead.");
        }

        if (feature.Geometry is null || feature.Geometry.IsEmpty)
        {
            return;
        }

        _tree.Insert(feature.Geometry.EnvelopeInternal, feature);
        _features.Add(feature);
        _extent = _extent.Union(feature.Extent);
    }

    /// <inheritdoc />
    public void Build()
    {
        if (_built)
        {
            return;
        }

        _tree.Build();
        _built = true;
    }

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> QueryBoundingBox(Extent extent)
    {
        EnsureBuilt();

        if (extent.IsEmpty)
        {
            return [];
        }

        return [.. _tree.Query(new Envelope(extent.MinX, extent.MaxX, extent.MinY, extent.MaxY))];
    }

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> QueryIntersects(NtsGeometry geometry) =>
        Refine(geometry, static (candidate, search) => candidate.Intersects(search));

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> QueryContains(NtsGeometry geometry) =>
        Refine(geometry, static (candidate, search) => candidate.Contains(search));

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> QueryWithin(NtsGeometry geometry) =>
        Refine(geometry, static (candidate, search) => candidate.Within(search));

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> QueryTouches(NtsGeometry geometry) =>
        Refine(geometry, static (candidate, search) => candidate.Touches(search));

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> QueryOverlaps(NtsGeometry geometry) =>
        Refine(geometry, static (candidate, search) => candidate.Overlaps(search));

    /// <summary>
    /// Finds the nearest features by expanding a search window until enough candidates appear.
    /// </summary>
    /// <remarks>
    /// Implemented over <c>Query</c> rather than the tree's own nearest-neighbour search so the
    /// result is ordered by true geometric distance rather than by envelope distance. For long
    /// diagonal features the two orderings genuinely differ, and the envelope answer is wrong.
    /// </remarks>
    /// <param name="geometry">The search geometry.</param>
    /// <param name="count">How many to return.</param>
    /// <returns>The nearest features, closest first.</returns>
    public IReadOnlyList<GisFeature> QueryNearest(NtsGeometry geometry, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        EnsureBuilt();

        if (_features.Count == 0)
        {
            return [];
        }

        Envelope search = geometry.EnvelopeInternal.Copy();
        double step = Math.Max(search.Width, search.Height);

        if (step <= 0d)
        {
            step = Math.Max(_extent.Width, _extent.Height) / 100d;
        }

        if (step <= 0d)
        {
            step = 1d;
        }

        IList<GisFeature> candidates = _tree.Query(search);

        // Expand geometrically until there are enough candidates or the whole extent is covered.
        for (int attempt = 0; candidates.Count < count && attempt < 32; attempt++)
        {
            search.ExpandBy(step);
            step *= 2d;
            candidates = _tree.Query(search);

            if (candidates.Count >= _features.Count)
            {
                break;
            }
        }

        if (candidates.Count == 0)
        {
            candidates = _features;
        }

        return [.. candidates
            .Select(feature => (Feature: feature, Distance: feature.Geometry!.Distance(geometry)))
            .OrderBy(static pair => pair.Distance)
            .Take(count)
            .Select(static pair => pair.Feature)];
    }

    private IReadOnlyList<GisFeature> Refine(NtsGeometry geometry, Func<NtsGeometry, NtsGeometry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        EnsureBuilt();

        if (geometry.IsEmpty)
        {
            return [];
        }

        IList<GisFeature> candidates = _tree.Query(geometry.EnvelopeInternal);
        List<GisFeature> matches = [];

        foreach (GisFeature candidate in candidates)
        {
            if (candidate.Geometry is not null && predicate(candidate.Geometry, geometry))
            {
                matches.Add(candidate);
            }
        }

        return matches;
    }

    private void EnsureBuilt()
    {
        if (!_built)
        {
            Build();
        }
    }
}
