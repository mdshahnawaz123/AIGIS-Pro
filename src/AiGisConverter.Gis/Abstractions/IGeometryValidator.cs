using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Gis.Profiles;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>Inspects a geometry against a profile's quality rules.</summary>
public interface IGeometryValidator
{
    /// <summary>Validates one geometry.</summary>
    /// <param name="geometry">The geometry to inspect. May be null.</param>
    /// <param name="featureId">The feature the geometry belongs to, for attribution.</param>
    /// <param name="rules">The rules to apply.</param>
    /// <returns>The findings. Empty when the geometry is sound.</returns>
    IReadOnlyList<ValidationIssue> Validate(NtsGeometry? geometry, string featureId, QualityRules rules);
}

/// <summary>Attempts to make an invalid geometry valid without changing what it means.</summary>
public interface IGeometryRepairer
{
    /// <summary>Repairs a geometry.</summary>
    /// <param name="geometry">The geometry to repair.</param>
    /// <returns>The outcome, describing what was done.</returns>
    GeometryRepairResult Repair(NtsGeometry geometry);
}

/// <summary>The outcome of a repair attempt.</summary>
/// <param name="Geometry">The repaired geometry, or null when repair failed.</param>
/// <param name="Succeeded">Whether a valid geometry was produced.</param>
/// <param name="Action">What was done, for the report.</param>
/// <param name="AreaChangeRatio">
/// How much the area moved, as a fraction of the original. Reported because a repair that changes
/// the area by more than a rounding error has changed the surveyed fact, not just its encoding.
/// </param>
public sealed record GeometryRepairResult(
    NtsGeometry? Geometry,
    bool Succeeded,
    string Action,
    double AreaChangeRatio)
{
    /// <summary>Creates a result for a geometry that needed nothing done.</summary>
    /// <param name="geometry">The unchanged geometry.</param>
    /// <returns>A successful result.</returns>
    public static GeometryRepairResult Unchanged(NtsGeometry geometry) =>
        new(geometry, true, "none required", 0d);

    /// <summary>Creates a result for a failed repair.</summary>
    /// <param name="action">What was attempted.</param>
    /// <returns>A failed result.</returns>
    public static GeometryRepairResult Failed(string action) => new(null, false, action, 0d);
}
