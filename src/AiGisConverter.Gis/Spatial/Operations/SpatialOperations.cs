using System.Runtime.CompilerServices;
using AiGisConverter.Domain.Common;
using AiGisConverter.Gis.Spatial.Abstractions;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.Operation.Union;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;
using NtsBufferParameters = NetTopologySuite.Operation.Buffer.BufferParameters;

namespace AiGisConverter.Gis.Spatial.Operations;

/// <summary>
/// Default <see cref="ISpatialOperations"/>.
/// </summary>
/// <remarks>
/// <para>
/// Every overlay is attempted twice. The first attempt is direct; if the noding fails &#8212; which
/// it does on near-coincident edges, the commonest defect in digitised survey data &#8212; the
/// inputs are passed through a zero-width buffer to rebuild their topology and the overlay is
/// retried. That recovers the large majority of real failures, and the retry is reported so the
/// caller knows the result came from repaired input.
/// </para>
/// <para>
/// Bulk union uses <see cref="UnaryUnionOp"/>, which unions a set in a cascade. Folding a set
/// pairwise is quadratic in the number of inputs and turns a district-wide dissolve from seconds
/// into hours.
/// </para>
/// </remarks>
public sealed class SpatialOperations : ISpatialOperations
{
    private static readonly GeometryFactory Factory = new();

    private readonly ILogger<SpatialOperations> _logger;

    /// <summary>Initializes a new instance of the <see cref="SpatialOperations"/> class.</summary>
    /// <param name="logger">Logger for overlay diagnostics.</param>
    public SpatialOperations(ILogger<SpatialOperations> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public Result<NtsGeometry> Buffer(NtsGeometry geometry, double distance, Abstractions.BufferParameters? parameters = null)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (!double.IsFinite(distance))
        {
            return Result.Failure<NtsGeometry>(new Error(
                "Spatial.InvalidDistance",
                "The buffer distance must be a finite number."));
        }

        Abstractions.BufferParameters settings = parameters ?? new Abstractions.BufferParameters();

        NtsBufferParameters native = new(settings.QuadrantSegments)
        {
            EndCapStyle = settings.EndCap switch
            {
                BufferEndCap.Square => NetTopologySuite.Operation.Buffer.EndCapStyle.Square,
                BufferEndCap.Flat => NetTopologySuite.Operation.Buffer.EndCapStyle.Flat,
                _ => NetTopologySuite.Operation.Buffer.EndCapStyle.Round,
            },
            JoinStyle = settings.JoinStyle switch
            {
                BufferJoin.Mitre => NetTopologySuite.Operation.Buffer.JoinStyle.Mitre,
                BufferJoin.Bevel => NetTopologySuite.Operation.Buffer.JoinStyle.Bevel,
                _ => NetTopologySuite.Operation.Buffer.JoinStyle.Round,
            },
            MitreLimit = settings.MitreLimit,
        };

        return Attempt(
            () => NetTopologySuite.Operation.Buffer.BufferOp.Buffer(geometry, distance, native),
            "buffer");
    }

    /// <inheritdoc />
    public Result<NtsGeometry> Union(NtsGeometry left, NtsGeometry right) =>
        Overlay(left, right, static (a, b) => a.Union(b), "union");

    /// <inheritdoc />
    public Result<NtsGeometry> Union(IEnumerable<NtsGeometry> geometries, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(geometries);

        List<NtsGeometry> usable = [];

        foreach (NtsGeometry geometry in geometries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (geometry is not null && !geometry.IsEmpty)
            {
                usable.Add(geometry);
            }
        }

        if (usable.Count == 0)
        {
            return Result.Success<NtsGeometry>(Factory.CreateGeometryCollection());
        }

        if (usable.Count == 1)
        {
            return Result.Success(usable[0]);
        }

        return Attempt(() => UnaryUnionOp.Union(usable), "cascaded union");
    }

    /// <inheritdoc />
    public Result<NtsGeometry> Intersection(NtsGeometry left, NtsGeometry right) =>
        Overlay(left, right, static (a, b) => a.Intersection(b), "intersection");

    /// <inheritdoc />
    public Result<NtsGeometry> Difference(NtsGeometry subject, NtsGeometry subtrahend) =>
        Overlay(subject, subtrahend, static (a, b) => a.Difference(b), "difference");

    /// <inheritdoc />
    public Result<NtsGeometry> SymmetricDifference(NtsGeometry left, NtsGeometry right) =>
        Overlay(left, right, static (a, b) => a.SymmetricDifference(b), "symmetric difference");

    /// <inheritdoc />
    public Result<IReadOnlyDictionary<string, NtsGeometry>> Dissolve<TSource>(
        IEnumerable<TSource> source,
        Func<TSource, string> keySelector,
        Func<TSource, NtsGeometry?> geometrySelector,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(keySelector);
        ArgumentNullException.ThrowIfNull(geometrySelector);

        Dictionary<string, List<NtsGeometry>> groups = new(StringComparer.OrdinalIgnoreCase);

        foreach (TSource element in source)
        {
            cancellationToken.ThrowIfCancellationRequested();

            NtsGeometry? geometry = geometrySelector(element);

            if (geometry is null || geometry.IsEmpty)
            {
                continue;
            }

            string key = keySelector(element);

            if (!groups.TryGetValue(key, out List<NtsGeometry>? bucket))
            {
                bucket = [];
                groups[key] = bucket;
            }

            bucket.Add(geometry);
        }

        Dictionary<string, NtsGeometry> dissolved = new(groups.Count, StringComparer.OrdinalIgnoreCase);
        List<string> failures = [];

        foreach (KeyValuePair<string, List<NtsGeometry>> group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Result<NtsGeometry> union = Union(group.Value, cancellationToken);

            if (union.IsSuccess)
            {
                dissolved[group.Key] = union.Value;
            }
            else
            {
                // One bad group must not lose the other forty. Record and continue.
                failures.Add(group.Key);
                _logger.LogError("Dissolve failed for group '{Key}': {Reason}", group.Key, union.Error.Message);
            }
        }

        if (failures.Count > 0 && dissolved.Count == 0)
        {
            return Result.Failure<IReadOnlyDictionary<string, NtsGeometry>>(new Error(
                "Spatial.DissolveFailed",
                $"Every group failed to dissolve: {string.Join(", ", failures)}."));
        }

        _logger.LogInformation(
            "Dissolved {InputCount} geometries into {GroupCount} groups ({FailureCount} groups failed).",
            groups.Sum(static g => g.Value.Count),
            dissolved.Count,
            failures.Count);

        return Result.Success<IReadOnlyDictionary<string, NtsGeometry>>(dissolved);
    }

    /// <inheritdoc />
    public Result<NtsGeometry> Merge(IEnumerable<NtsGeometry> geometries)
    {
        ArgumentNullException.ThrowIfNull(geometries);

        NtsGeometry[] parts = [.. geometries.Where(static g => g is not null && !g.IsEmpty)];

        if (parts.Length == 0)
        {
            return Result.Success<NtsGeometry>(Factory.CreateGeometryCollection());
        }

        // A homogeneous set becomes the matching multi-part type, which most formats can store.
        // A mixed set can only be a GeometryCollection, which many cannot - the caller is told by
        // the type of the result rather than by a failure.
        bool allPoints = parts.All(static p => p is Point);
        bool allLines = parts.All(static p => p is LineString);
        bool allPolygons = parts.All(static p => p is Polygon);

        NtsGeometry merged = true switch
        {
            _ when allPoints => Factory.CreateMultiPoint([.. parts.Cast<Point>()]),
            _ when allLines => Factory.CreateMultiLineString([.. parts.Cast<LineString>()]),
            _ when allPolygons => Factory.CreateMultiPolygon([.. parts.Cast<Polygon>()]),
            _ => Factory.CreateGeometryCollection(parts),
        };

        return Result.Success(merged);
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<NtsGeometry> ClipAsync(
        IAsyncEnumerable<NtsGeometry> geometries,
        NtsGeometry boundary,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(geometries);
        ArgumentNullException.ThrowIfNull(boundary);

        Envelope boundaryEnvelope = boundary.EnvelopeInternal;
        PreparedGeometryHolder prepared = new(boundary);

        await foreach (NtsGeometry geometry in geometries.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (geometry is null || geometry.IsEmpty)
            {
                continue;
            }

            // Envelope rejection first: it is a handful of comparisons and eliminates most of the
            // input without ever entering the overlay machinery.
            if (!boundaryEnvelope.Intersects(geometry.EnvelopeInternal))
            {
                continue;
            }

            if (prepared.Covers(geometry))
            {
                yield return geometry;
                continue;
            }

            Result<NtsGeometry> clipped = Intersection(geometry, boundary);

            if (clipped.IsSuccess && !clipped.Value.IsEmpty)
            {
                yield return clipped.Value;
            }
        }
    }

    /// <summary>Runs an overlay, retrying through repaired inputs when the noding fails.</summary>
    private Result<NtsGeometry> Overlay(
        NtsGeometry left,
        NtsGeometry right,
        Func<NtsGeometry, NtsGeometry, NtsGeometry> operation,
        string name)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        try
        {
            return Result.Success(operation(left, right));
        }
        catch (TopologyException)
        {
            _logger.LogWarning("The {Operation} failed on the first attempt; retrying with repaired inputs.", name);
        }

        try
        {
            NtsGeometry repairedLeft = left.IsValid ? left : left.Buffer(0d);
            NtsGeometry repairedRight = right.IsValid ? right : right.Buffer(0d);

            return Result.Success(operation(repairedLeft, repairedRight));
        }
        catch (TopologyException ex)
        {
            return Result.Failure<NtsGeometry>(new Error(
                $"Spatial.{name.Replace(" ", string.Empty, StringComparison.Ordinal)}Failed",
                $"The {name} could not be computed even after repairing the inputs: {ex.Message}"));
        }
    }

    private Result<NtsGeometry> Attempt(Func<NtsGeometry> operation, string name)
    {
        try
        {
            return Result.Success(operation());
        }
        catch (TopologyException ex)
        {
            return Result.Failure<NtsGeometry>(new Error(
                $"Spatial.{name.Replace(" ", string.Empty, StringComparison.Ordinal)}Failed",
                $"The {name} could not be computed: {ex.Message}"));
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<NtsGeometry>(new Error(
                $"Spatial.{name.Replace(" ", string.Empty, StringComparison.Ordinal)}Invalid",
                ex.Message));
        }
    }

    /// <summary>
    /// Caches a prepared form of the clip boundary.
    /// </summary>
    /// <remarks>
    /// Preparing indexes the boundary's edges once. Against a stream of a million features that
    /// turns a repeated full traversal of the boundary into a repeated index lookup.
    /// </remarks>
    private sealed class PreparedGeometryHolder
    {
        private readonly NetTopologySuite.Geometries.Prepared.IPreparedGeometry _prepared;

        public PreparedGeometryHolder(NtsGeometry boundary) =>
            _prepared = NetTopologySuite.Geometries.Prepared.PreparedGeometryFactory.Prepare(boundary);

        public bool Covers(NtsGeometry geometry)
        {
            try
            {
                return _prepared.Covers(geometry);
            }
            catch (TopologyException)
            {
                return false;
            }
        }
    }
}
