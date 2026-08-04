// GDAL BOUNDARY FILE (3 of 4). See Gdal/GdalEnvironment.cs.

using System.Collections.Concurrent;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Gdal;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NetTopologySuite.Geometries;
using OSGeo.OSR;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Crs;

/// <summary>
/// Reprojects geometry through PROJ.
/// </summary>
/// <remarks>
/// <para>
/// Transformation is done on the coordinate arrays rather than by round-tripping geometry through
/// OGR. A WKB round trip per feature would allocate twice the geometry and marshal it across the
/// native boundary twice; transforming the ordinates in bulk touches native code once per feature
/// and leaves the NetTopologySuite structure intact.
/// </para>
/// <para>
/// Axis order is forced to traditional x/y unless configuration says otherwise. EPSG defines 4326
/// as latitude-then-longitude, and every GeoJSON file in existence is the other way round; taking
/// the authority at its word is how output ends up in the Indian Ocean.
/// </para>
/// </remarks>
public sealed class GdalCoordinateTransformer : ICoordinateTransformer, IDisposable
{
    private readonly ConcurrentDictionary<string, CoordinateTransformation?> _cache = new(StringComparer.Ordinal);
    private readonly GdalEnvironment _environment;
    private readonly ICrsRegistry _registry;
    private readonly IOptionsMonitor<GisOptions> _options;
    private readonly ILogger<GdalCoordinateTransformer> _logger;

    /// <summary>Initializes a new instance of the <see cref="GdalCoordinateTransformer"/> class.</summary>
    /// <param name="environment">The native library gate.</param>
    /// <param name="registry">Supplies system definitions.</param>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for transformation diagnostics.</param>
    public GdalCoordinateTransformer(
        GdalEnvironment environment,
        ICrsRegistry registry,
        IOptionsMonitor<GisOptions> options,
        ILogger<GdalCoordinateTransformer> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _environment = environment;
        _registry = registry;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public bool CanTransform(CoordinateSystem source, CoordinateSystem target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        return source == target || (_environment.Ensure() && GetTransformation(source, target) is not null);
    }

    /// <inheritdoc />
    public Result<NtsGeometry> Transform(NtsGeometry geometry, CoordinateSystem source, CoordinateSystem target)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (source == target || geometry.IsEmpty)
        {
            return Result.Success(geometry);
        }

        if (!_environment.Ensure())
        {
            return Result.Failure<NtsGeometry>(new Error("Crs.GdalUnavailable", _environment.FailureReason!));
        }

        CoordinateTransformation? transformation = GetTransformation(source, target);

        if (transformation is null)
        {
            return Result.Failure<NtsGeometry>(new Error(
                "Crs.NoTransformation",
                $"PROJ has no transformation from {source.Identifier} to {target.Identifier}."));
        }

        try
        {
            NtsGeometry copy = geometry.Copy();
            copy.Apply(new TransformFilter(transformation));
            copy.GeometryChanged();

            return Result.Success(copy);
        }
        catch (Exception ex) when (ex is ApplicationException or InvalidOperationException)
        {
            return Result.Failure<NtsGeometry>(new Error("Crs.TransformationFailed", ex.Message));
        }
    }

    /// <inheritdoc />
    public Result<Extent> Transform(Extent extent, CoordinateSystem source, CoordinateSystem target)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        if (extent.IsEmpty || source == target)
        {
            return Result.Success(extent);
        }

        if (!_environment.Ensure())
        {
            return Result.Failure<Extent>(new Error("Crs.GdalUnavailable", _environment.FailureReason!));
        }

        CoordinateTransformation? transformation = GetTransformation(source, target);

        if (transformation is null)
        {
            return Result.Failure<Extent>(new Error(
                "Crs.NoTransformation",
                $"PROJ has no transformation from {source.Identifier} to {target.Identifier}."));
        }

        // All four corners are transformed, not just two. A projected box does not stay a box, and
        // taking only the diagonal corners under-reports the extent along the curved edges.
        double[] xs = [extent.MinX, extent.MaxX, extent.MinX, extent.MaxX];
        double[] ys = [extent.MinY, extent.MinY, extent.MaxY, extent.MaxY];
        double[] zs = [0d, 0d, 0d, 0d];

        try
        {
            transformation.TransformPoints(4, xs, ys, zs);

            return Result.Success(Extent.Create(xs.Min(), ys.Min(), xs.Max(), ys.Max()));
        }
        catch (Exception ex) when (ex is ApplicationException or InvalidOperationException)
        {
            return Result.Failure<Extent>(new Error("Crs.TransformationFailed", ex.Message));
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (CoordinateTransformation? transformation in _cache.Values)
        {
            transformation?.Dispose();
        }

        _cache.Clear();
    }

    private CoordinateTransformation? GetTransformation(CoordinateSystem source, CoordinateSystem target)
    {
        string key = $"{source.Identifier}->{target.Identifier}";

        if (!_options.CurrentValue.Crs.CacheTransformations)
        {
            return Build(source, target);
        }

        if (_cache.TryGetValue(key, out CoordinateTransformation? cached))
        {
            return cached;
        }

        CoordinateTransformation? built = Build(source, target);

        // Only successes are cached. Caching a failure would make a transient condition — most
        // often the native stack not yet being initialised on the very first call — permanent for
        // the life of the process, so a pair that works perfectly well would be reported as
        // unavailable forever after.
        if (built is not null)
        {
            built = _cache.GetOrAdd(key, built);
        }

        return built;
    }

    private CoordinateTransformation? Build(CoordinateSystem source, CoordinateSystem target)
    {
        // Not disposed: PROJ's transformation object refers to these definitions for its lifetime,
        // and the transformation itself is cached and reused. Disposing them here left the cached
        // transformation pointing at freed native memory.
        SpatialReference? from = null;
        SpatialReference? to = null;

        try
        {
            from = BuildReference(source);
            to = BuildReference(target);

            CoordinateTransformation transformation = new(from, to);

            _logger.LogInformation(
                "Built transformation {Source} -> {Target}.",
                source.Identifier,
                target.Identifier);

            return transformation;
        }
        catch (Exception ex)
        {
            // Deliberately broad: the SWIG bindings surface PROJ failures through several
            // exception types, and swallowing only two of them turned a diagnosable error into a
            // silent "no transformation available".
            from?.Dispose();
            to?.Dispose();

            _logger.LogError(
                ex,
                "Could not build a transformation from {Source} to {Target}: {Message}",
                source.Identifier,
                target.Identifier,
                ex.Message);

            return null;
        }
    }

    private SpatialReference BuildReference(CoordinateSystem coordinateSystem)
    {
        SpatialReference reference = new(string.Empty);
        Result<string> wkt = _registry.GetWellKnownText(coordinateSystem);

        if (wkt.IsSuccess)
        {
            string definition = wkt.Value;
            reference.ImportFromWkt(ref definition);
        }
        else
        {
            reference.SetFromUserInput(coordinateSystem.Identifier);
        }

        if (!_options.CurrentValue.Crs.UseAuthorityAxisOrder)
        {
            reference.SetAxisMappingStrategy(AxisMappingStrategy.OAMS_TRADITIONAL_GIS_ORDER);
        }

        return reference;
    }

    /// <summary>Transforms every ordinate of a geometry in place.</summary>
    private sealed class TransformFilter : ICoordinateSequenceFilter
    {
        private readonly CoordinateTransformation _transformation;

        public TransformFilter(CoordinateTransformation transformation) => _transformation = transformation;

        public bool Done => false;

        public bool GeometryChanged => true;

        public void Filter(CoordinateSequence sequence, int index)
        {
            double[] x = [sequence.GetX(index)];
            double[] y = [sequence.GetY(index)];
            double[] z = [sequence.HasZ && !double.IsNaN(sequence.GetZ(index)) ? sequence.GetZ(index) : 0d];

            _transformation.TransformPoints(1, x, y, z);

            sequence.SetX(index, x[0]);
            sequence.SetY(index, y[0]);

            if (sequence.HasZ)
            {
                sequence.SetZ(index, z[0]);
            }
        }
    }
}
