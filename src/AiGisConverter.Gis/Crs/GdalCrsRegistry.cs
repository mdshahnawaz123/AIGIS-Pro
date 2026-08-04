// GDAL BOUNDARY FILE (2 of 4). See Gdal/GdalEnvironment.cs.

using System.Collections.Concurrent;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Gdal;
using Microsoft.Extensions.Logging;
using OSGeo.OSR;

namespace AiGisConverter.Gis.Crs;

/// <summary>
/// Resolves coordinate systems against PROJ's own EPSG database.
/// </summary>
/// <remarks>
/// <para>
/// There are roughly seven thousand EPSG systems and the registry is revised several times a year.
/// A lookup table in this codebase would be wrong the day it was written and silently wrong
/// thereafter, so every answer here comes from PROJ.
/// </para>
/// <para>
/// Resolutions are cached by identifier. Constructing a <see cref="SpatialReference"/> parses a
/// definition and touches the on-disk database; doing that per feature would dominate the run.
/// </para>
/// </remarks>
public sealed class GdalCrsRegistry : ICrsRegistry
{
    private readonly ConcurrentDictionary<string, Result<CoordinateSystem>> _resolved =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, string> _wellKnownText =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly GdalEnvironment _environment;
    private readonly ILogger<GdalCrsRegistry> _logger;

    /// <summary>Initializes a new instance of the <see cref="GdalCrsRegistry"/> class.</summary>
    /// <param name="environment">The native library gate.</param>
    /// <param name="logger">Logger for resolution diagnostics.</param>
    public GdalCrsRegistry(GdalEnvironment environment, ILogger<GdalCrsRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(logger);

        _environment = environment;
        _logger = logger;
    }

    /// <inheritdoc />
    public Result<CoordinateSystem> Resolve(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return Result.Failure<CoordinateSystem>(new Error(
                "Crs.NotSpecified",
                "No coordinate system was named."));
        }

        return _resolved.GetOrAdd(identifier.Trim(), ResolveCore);
    }

    /// <inheritdoc />
    public Result<string> GetWellKnownText(CoordinateSystem coordinateSystem)
    {
        ArgumentNullException.ThrowIfNull(coordinateSystem);

        if (coordinateSystem.WellKnownText is { Length: > 0 } embedded)
        {
            return Result.Success(embedded);
        }

        if (_wellKnownText.TryGetValue(coordinateSystem.Identifier, out string? cached))
        {
            return Result.Success(cached);
        }

        if (!_environment.Ensure())
        {
            return Result.Failure<string>(new Error("Crs.GdalUnavailable", _environment.FailureReason!));
        }

        try
        {
            using SpatialReference reference = Create(coordinateSystem);
            reference.ExportToWkt(out string wkt, []);

            if (string.IsNullOrWhiteSpace(wkt))
            {
                return Result.Failure<string>(new Error(
                    "Crs.NoDefinition",
                    $"PROJ has no definition for {coordinateSystem.Identifier}."));
            }

            _wellKnownText[coordinateSystem.Identifier] = wkt;

            return Result.Success(wkt);
        }
        catch (Exception ex) when (ex is ApplicationException or InvalidOperationException)
        {
            return Result.Failure<string>(new Error("Crs.DefinitionFailed", ex.Message));
        }
    }

    /// <inheritdoc />
    public bool IsGeographic(CoordinateSystem coordinateSystem)
    {
        ArgumentNullException.ThrowIfNull(coordinateSystem);

        if (!_environment.Ensure())
        {
            // The parser's heuristic is the only answer available without PROJ.
            return coordinateSystem.IsGeographic;
        }

        try
        {
            using SpatialReference reference = Create(coordinateSystem);

            return reference.IsGeographic() == 1;
        }
        catch (Exception ex) when (ex is ApplicationException or InvalidOperationException)
        {
            return coordinateSystem.IsGeographic;
        }
    }

    private Result<CoordinateSystem> ResolveCore(string identifier)
    {
        if (!_environment.Ensure())
        {
            // Fall back to the syntactic parser so an EPSG code still works for formats that do
            // not need PROJ. Anything requiring a datum shift will fail later, explicitly.
            return CoordinateSystem.TryParse(identifier, out CoordinateSystem? parsed)
                ? Result.Success(parsed!)
                : Result.Failure<CoordinateSystem>(new Error("Crs.GdalUnavailable", _environment.FailureReason!));
        }

        try
        {
            using SpatialReference reference = new(string.Empty);
            int status = identifier.Contains('[', StringComparison.Ordinal)
                ? reference.ImportFromWkt(ref identifier)
                : reference.SetFromUserInput(identifier);

            if (status != 0)
            {
                return Result.Failure<CoordinateSystem>(new Error(
                    "Crs.Unrecognised",
                    $"'{identifier}' is not a coordinate system PROJ recognises."));
            }

            reference.AutoIdentifyEPSG();

            string? authority = reference.GetAuthorityName(null);
            string? code = reference.GetAuthorityCode(null);
            bool geographic = reference.IsGeographic() == 1;

            reference.ExportToWkt(out string wkt, []);

            CoordinateSystem system = authority is { Length: > 0 } && int.TryParse(code, out int numeric)
                ? CoordinateSystem.Create(authority, numeric, reference.GetName(), geographic)
                : CoordinateSystem.Create("CUSTOM", StableCustomCode(wkt), reference.GetName(), geographic);

            system = system with { WellKnownText = wkt };
            _wellKnownText[system.Identifier] = wkt;

            _logger.LogDebug("Resolved {Identifier} to {Crs}.", identifier, system.Identifier);

            return Result.Success(system);
        }
        catch (Exception ex) when (ex is ApplicationException or InvalidOperationException)
        {
            return Result.Failure<CoordinateSystem>(new Error("Crs.ResolutionFailed", ex.Message));
        }
    }

    /// <summary>
    /// Derives a stable positive code for a custom system with no authority.
    /// </summary>
    /// <remarks>
    /// A custom projection still needs an identity so it can be compared, cached and written to a
    /// run record. The code is derived from the definition rather than allocated, so the same
    /// custom system resolves to the same identifier on every machine and every run &#8212; which
    /// a counter or a GUID would not.
    /// </remarks>
    private static int StableCustomCode(string wellKnownText)
    {
        // FNV-1a, taken positive. Chosen over string.GetHashCode, which is randomised per process.
        const uint OffsetBasis = 2166136261;
        const uint Prime = 16777619;

        uint hash = OffsetBasis;

        foreach (char character in wellKnownText)
        {
            hash ^= character;
            hash *= Prime;
        }

        return (int)(hash & 0x7FFFFFFF) | 1;
    }

    /// <summary>Builds a native spatial reference from a domain value.</summary>
    private static SpatialReference Create(CoordinateSystem coordinateSystem)
    {
        SpatialReference reference = new(string.Empty);

        if (coordinateSystem.WellKnownText is { Length: > 0 } wkt)
        {
            string definition = wkt;
            reference.ImportFromWkt(ref definition);
        }
        else
        {
            reference.SetFromUserInput(coordinateSystem.Identifier);
        }

        return reference;
    }
}
