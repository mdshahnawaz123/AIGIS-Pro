using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Resolves coordinate system identifiers into definitions.
/// </summary>
/// <remarks>
/// Backed by PROJ's own EPSG database rather than by a table in this codebase. There are roughly
/// seven thousand EPSG systems and they are revised: a hardcoded list is wrong the day it is
/// written, and silently wrong thereafter.
/// </remarks>
public interface ICrsRegistry
{
    /// <summary>Resolves an identifier such as <c>EPSG:27700</c> into a full definition.</summary>
    /// <param name="identifier">The identifier, or a raw WKT definition.</param>
    /// <returns>The resolved system, or a failure when it is not recognised.</returns>
    Result<CoordinateSystem> Resolve(string identifier);

    /// <summary>Gets the WKT definition for a system, for writing a <c>.prj</c> sidecar.</summary>
    /// <param name="coordinateSystem">The system.</param>
    /// <returns>The definition, or a failure when it cannot be produced.</returns>
    Result<string> GetWellKnownText(CoordinateSystem coordinateSystem);

    /// <summary>Determines whether a system uses angular units.</summary>
    /// <param name="coordinateSystem">The system.</param>
    /// <returns><see langword="true"/> when coordinates are degrees rather than linear units.</returns>
    bool IsGeographic(CoordinateSystem coordinateSystem);
}
