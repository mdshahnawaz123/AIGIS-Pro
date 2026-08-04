using AiGisConverter.Domain.Common;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Abstractions;

/// <summary>
/// Closes digitising noise by snapping near-coincident vertices together.
/// </summary>
/// <remarks>
/// Separate from <c>IGeometryRepairer</c>, which fixes geometry that is structurally invalid.
/// Snapping operates on geometry that is perfectly valid and merely imprecise, and it is
/// destructive, so it is opt-in with an explicitly chosen tolerance rather than part of the
/// default repair path.
/// </remarks>
public interface IGeometrySnapper
{
    /// <summary>Nodes a geometry against itself on a fixed precision grid.</summary>
    /// <param name="geometry">The geometry to snap.</param>
    /// <param name="tolerance">The grid size. Zero returns the input unchanged.</param>
    /// <returns>The snapped geometry, or a failure when the tolerance destroys it.</returns>
    Result<NtsGeometry> SnapToSelf(NtsGeometry geometry, double tolerance);

    /// <summary>Snaps a geometry's vertices onto a reference geometry's.</summary>
    /// <param name="geometry">The geometry to move.</param>
    /// <param name="reference">The geometry to snap towards.</param>
    /// <param name="tolerance">The maximum distance a vertex may move.</param>
    /// <returns>The snapped geometry.</returns>
    Result<NtsGeometry> SnapTo(NtsGeometry geometry, NtsGeometry reference, double tolerance);

    /// <summary>Removes consecutive vertices closer together than the tolerance.</summary>
    /// <param name="geometry">The geometry to clean.</param>
    /// <param name="tolerance">The minimum permitted vertex separation.</param>
    /// <returns>The cleaned geometry, or the input when cleaning would destroy it.</returns>
    NtsGeometry RemoveDuplicateVertices(NtsGeometry geometry, double tolerance);
}
