using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Spatial.Abstractions;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Query;

/// <summary>
/// Default <see cref="ISpatialQueryEngine"/>.
/// </summary>
/// <remarks>
/// Owns its index rather than accepting one, so the coordinate system the index was loaded under
/// cannot drift from the one distances are computed in. Mixing those is how a radius search
/// returns everything or nothing.
/// </remarks>
public sealed class SpatialQueryEngine : ISpatialQueryEngine
{
    /// <summary>
    /// Metres per degree of latitude, used only to size the candidate envelope for a radius query.
    /// </summary>
    /// <remarks>
    /// Deliberately the smaller of the two axes. Over-selecting candidates costs a few exact
    /// distance evaluations; under-selecting silently drops real hits.
    /// </remarks>
    private const double MetresPerDegree = 111_320d;

    private readonly ISpatialIndex _index;
    private readonly ISpatialAnalysis _analysis;
    private readonly ITopologyEngine _topology;
    private readonly ILogger<SpatialQueryEngine> _logger;
    private readonly GeometryFactory _factory = new();

    private CoordinateSystem _coordinateSystem = CoordinateSystem.Wgs84;
    private bool _isGeographic;

    /// <summary>Initializes a new instance of the <see cref="SpatialQueryEngine"/> class.</summary>
    /// <param name="index">The R-tree.</param>
    /// <param name="analysis">Supplies distance measurement.</param>
    /// <param name="topology">Supplies the predicates.</param>
    /// <param name="crsRegistry">Decides whether the loaded system is geographic.</param>
    /// <param name="logger">Logger for query diagnostics.</param>
    public SpatialQueryEngine(
        ISpatialIndex index,
        ISpatialAnalysis analysis,
        ITopologyEngine topology,
        ICrsRegistry crsRegistry,
        ILogger<SpatialQueryEngine> logger)
    {
        ArgumentNullException.ThrowIfNull(index);
        ArgumentNullException.ThrowIfNull(analysis);
        ArgumentNullException.ThrowIfNull(topology);
        ArgumentNullException.ThrowIfNull(crsRegistry);
        ArgumentNullException.ThrowIfNull(logger);

        _index = index;
        _analysis = analysis;
        _topology = topology;
        _crsRegistry = crsRegistry;
        _logger = logger;
    }

    private readonly ICrsRegistry _crsRegistry;

    /// <inheritdoc />
    public int Count => _index.Count;

    /// <inheritdoc />
    public Extent Extent => _index.Extent;

    /// <inheritdoc />
    public void Load(
        IEnumerable<GisFeature> features,
        CoordinateSystem coordinateSystem,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(coordinateSystem);

        _coordinateSystem = coordinateSystem;
        _isGeographic = _crsRegistry.IsGeographic(coordinateSystem);

        foreach (GisFeature feature in features)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _index.Insert(feature);
        }

        _index.Build();

        _logger.LogInformation(
            "Spatial index built over {Count} features in {Crs} ({Kind}).",
            _index.Count,
            coordinateSystem.Identifier,
            _isGeographic ? "geographic" : "projected");
    }

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> QueryBoundingBox(Extent extent) => _index.QueryBoundingBox(extent);

    /// <inheritdoc />
    public IReadOnlyList<FeatureDistance> QueryRadius(double x, double y, double radius)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(radius);

        Point centre = _factory.CreatePoint(new Coordinate(x, y));

        // An envelope in the index's own units, sized so it cannot exclude a genuine hit.
        double envelopeRadius = _isGeographic ? radius / MetresPerDegree : radius;
        Extent search = Extent.Create(x - envelopeRadius, y - envelopeRadius, x + envelopeRadius, y + envelopeRadius);

        List<FeatureDistance> matches = [];

        foreach (GisFeature candidate in _index.QueryBoundingBox(search))
        {
            if (candidate.Geometry is null || candidate.Geometry.IsEmpty)
            {
                continue;
            }

            Domain.Common.Result<Measurement> distance =
                _analysis.Distance(centre, candidate.Geometry, _coordinateSystem);

            if (distance.IsSuccess && distance.Value.Value <= radius)
            {
                matches.Add(new FeatureDistance(candidate, distance.Value));
            }
        }

        return [.. matches.OrderBy(static m => m.Distance.Value)];
    }

    /// <inheritdoc />
    public IReadOnlyList<FeatureDistance> QueryNearest(NtsGeometry geometry, int count = 1)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        List<FeatureDistance> results = [];

        foreach (GisFeature feature in _index.QueryNearest(geometry, count))
        {
            if (feature.Geometry is null)
            {
                continue;
            }

            Domain.Common.Result<Measurement> distance =
                _analysis.Distance(geometry, feature.Geometry, _coordinateSystem);

            if (distance.IsSuccess)
            {
                results.Add(new FeatureDistance(feature, distance.Value));
            }
        }

        return [.. results.OrderBy(static r => r.Distance.Value)];
    }

    /// <inheritdoc />
    public IReadOnlyList<GisFeature> Query(NtsGeometry geometry, SpatialPredicate predicate)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        // Disjoint is the one predicate an R-tree cannot narrow: a feature outside the search
        // envelope is exactly the answer, so the whole set has to be considered.
        IReadOnlyList<GisFeature> candidates = predicate == SpatialPredicate.Disjoint
            ? _index.QueryBoundingBox(_index.Extent)
            : _index.QueryBoundingBox(BoundingBoxOf(geometry));

        List<GisFeature> matches = [];

        foreach (GisFeature candidate in candidates)
        {
            if (candidate.Geometry is null)
            {
                continue;
            }

            bool satisfied = predicate switch
            {
                SpatialPredicate.Intersects => _topology.Intersects(candidate.Geometry, geometry),
                SpatialPredicate.Touches => _topology.Touches(candidate.Geometry, geometry),
                SpatialPredicate.Within => _topology.Within(candidate.Geometry, geometry),
                SpatialPredicate.Contains => _topology.Contains(candidate.Geometry, geometry),
                SpatialPredicate.Overlaps => _topology.Overlaps(candidate.Geometry, geometry),
                SpatialPredicate.Crosses => _topology.Crosses(candidate.Geometry, geometry),
                SpatialPredicate.Disjoint => _topology.Disjoint(candidate.Geometry, geometry),
                _ => false,
            };

            if (satisfied)
            {
                matches.Add(candidate);
            }
        }

        return matches;
    }

    private static Extent BoundingBoxOf(NtsGeometry geometry)
    {
        Envelope envelope = geometry.EnvelopeInternal;

        return envelope.IsNull
            ? Extent.Empty
            : Extent.Create(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
    }
}
