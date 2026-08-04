using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Services;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Spatial.Abstractions;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Analysis;

/// <summary>
/// Default <see cref="ISpatialAnalysis"/>.
/// </summary>
/// <remarks>
/// The one decision this class exists to make: whether a measurement is planar or geodesic. It
/// asks the CRS registry, computes accordingly, and labels the result. Nothing downstream has to
/// remember to check, and no caller can accidentally report square degrees as an area.
/// </remarks>
public sealed class SpatialAnalysis : ISpatialAnalysis
{
    private const string SquareMetres = "square metre";
    private const string Metres = "metre";

    private readonly ICrsRegistry _crsRegistry;

    /// <summary>Initializes a new instance of the <see cref="SpatialAnalysis"/> class.</summary>
    /// <param name="crsRegistry">Decides whether a system is geographic.</param>
    public SpatialAnalysis(ICrsRegistry crsRegistry)
    {
        ArgumentNullException.ThrowIfNull(crsRegistry);
        _crsRegistry = crsRegistry;
    }

    /// <inheritdoc />
    public Result<Measurement> Area(NtsGeometry geometry, CoordinateSystem coordinateSystem)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(coordinateSystem);

        if (geometry.IsEmpty)
        {
            return Result.Success(new Measurement(0d, SquareMetres, false));
        }

        if (_crsRegistry.IsGeographic(coordinateSystem))
        {
            return Result.Success(new Measurement(
                GeodeticCalculator.Area(geometry),
                SquareMetres,
                IsGeodetic: true,
                GeodeticCalculator.AccuracyNote));
        }

        return Result.Success(new Measurement(
            geometry.Area,
            $"square {UnitName(coordinateSystem)}",
            IsGeodetic: false));
    }

    /// <inheritdoc />
    public Result<Measurement> Length(NtsGeometry geometry, CoordinateSystem coordinateSystem)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        ArgumentNullException.ThrowIfNull(coordinateSystem);

        if (geometry.IsEmpty)
        {
            return Result.Success(new Measurement(0d, Metres, false));
        }

        if (_crsRegistry.IsGeographic(coordinateSystem))
        {
            return Result.Success(new Measurement(
                GeodeticCalculator.Length(geometry),
                Metres,
                IsGeodetic: true,
                GeodeticCalculator.AccuracyNote));
        }

        return Result.Success(new Measurement(geometry.Length, UnitName(coordinateSystem), IsGeodetic: false));
    }

    /// <inheritdoc />
    public Result<Measurement> Distance(NtsGeometry left, NtsGeometry right, CoordinateSystem coordinateSystem)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentNullException.ThrowIfNull(coordinateSystem);

        if (left.IsEmpty || right.IsEmpty)
        {
            return Result.Failure<Measurement>(new Error(
                "Spatial.EmptyGeometry",
                "Distance is undefined when either geometry is empty."));
        }

        if (!_crsRegistry.IsGeographic(coordinateSystem))
        {
            return Result.Success(new Measurement(
                left.Distance(right),
                UnitName(coordinateSystem),
                IsGeodetic: false));
        }

        // NetTopologySuite finds the nearest pair in the plane; on degrees that pair is very
        // nearly the geodesic nearest pair too, and measuring it geodesically is correct where
        // simply reporting the planar separation would not be.
        NetTopologySuite.Operation.Distance.DistanceOp operation = new(left, right);
        Coordinate[] nearest = operation.NearestPoints();

        return Result.Success(new Measurement(
            GeodeticCalculator.Distance(nearest[0].X, nearest[0].Y, nearest[1].X, nearest[1].Y),
            Metres,
            IsGeodetic: true,
            GeodeticCalculator.AccuracyNote));
    }

    /// <inheritdoc />
    public Point? Centroid(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return geometry.IsEmpty ? null : geometry.Centroid;
    }

    /// <inheritdoc />
    public Point? PointOnSurface(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return geometry.IsEmpty ? null : geometry.InteriorPoint;
    }

    /// <inheritdoc />
    public Extent BoundingBox(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        if (geometry.IsEmpty)
        {
            return Extent.Empty;
        }

        Envelope envelope = geometry.EnvelopeInternal;

        return Extent.Create(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
    }

    /// <inheritdoc />
    public NtsGeometry ConvexHull(NtsGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);

        return geometry.ConvexHull();
    }

    /// <inheritdoc />
    public IReadOnlyList<NearestResult> Nearest(
        NtsGeometry target,
        IEnumerable<NtsGeometry> candidates,
        CoordinateSystem coordinateSystem,
        int count = 1)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        List<NearestResult> results = [];

        foreach (NtsGeometry candidate in candidates)
        {
            if (candidate is null || candidate.IsEmpty)
            {
                continue;
            }

            Result<Measurement> distance = Distance(target, candidate, coordinateSystem);

            if (distance.IsSuccess)
            {
                results.Add(new NearestResult(candidate, distance.Value));
            }
        }

        return [.. results.OrderBy(static r => r.Distance.Value).Take(count)];
    }

    /// <summary>Names the linear unit of a projected system, for labelling the result.</summary>
    /// <remarks>
    /// Reported rather than converted. Silently normalising a US-survey-foot state-plane figure to
    /// metres would hide the unit the source actually used, which is the information a surveyor
    /// needs to reconcile it against their own totals.
    /// </remarks>
    private string UnitName(CoordinateSystem coordinateSystem)
    {
        Result<string> wkt = _crsRegistry.GetWellKnownText(coordinateSystem);

        if (wkt.IsFailure)
        {
            return "linear unit";
        }

        string definition = wkt.Value;

        if (definition.Contains("US survey foot", StringComparison.OrdinalIgnoreCase))
        {
            return LinearUnitConverter.IsLinear(LinearUnit.UsSurveyFoot) ? "US survey foot" : "linear unit";
        }

        if (definition.Contains("\"foot\"", StringComparison.OrdinalIgnoreCase))
        {
            return "foot";
        }

        return definition.Contains("\"metre\"", StringComparison.OrdinalIgnoreCase)
               || definition.Contains("\"meter\"", StringComparison.OrdinalIgnoreCase)
            ? Metres
            : "linear unit";
    }
}
