using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;

namespace AiGisConverter.Gis.Crs;

/// <summary>
/// Default <see cref="ICrsValidator"/>: the pre-flight checks that run before a conversion.
/// </summary>
/// <remarks>
/// Only genuinely blocking conditions are errors — a missing system, or a transformation the PROJ
/// stack cannot perform. Everything else is a warning, because a surveyor with local knowledge is
/// often right when a heuristic is doubtful, and a tool that refuses plausible work is a tool that
/// gets worked around.
/// </remarks>
public sealed class CrsValidator : ICrsValidator
{
    private readonly ICoordinateTransformer _transformer;
    private readonly ICrsCatalog _catalog;

    /// <summary>Initializes a new instance of the <see cref="CrsValidator"/> class.</summary>
    /// <param name="transformer">Used to test whether a transformation is actually available.</param>
    /// <param name="catalog">Supplies each system's area of use and units.</param>
    public CrsValidator(ICoordinateTransformer transformer, ICrsCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(transformer);
        ArgumentNullException.ThrowIfNull(catalog);
        _transformer = transformer;
        _catalog = catalog;
    }

    /// <inheritdoc />
    public async Task<CrsValidationReport> ValidateAsync(
        CrsValidationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        List<CrsValidationFinding> findings = [];

        CoordinateSystem? input = request.InputCrs;
        CoordinateSystem? output = request.OutputCrs;

        // --- selection ---------------------------------------------------------------------
        if (input is null)
        {
            findings.Add(new(CrsValidationSeverity.Error, "Input CRS",
                "No input coordinate system is selected. Choose one, or accept a detected suggestion."));
        }
        else
        {
            findings.Add(new(CrsValidationSeverity.Information, "Input CRS", $"{input.Identifier} selected."));
        }

        if (output is null)
        {
            findings.Add(new(CrsValidationSeverity.Error, "Output CRS", "No output coordinate system is selected."));
        }
        else
        {
            findings.Add(new(CrsValidationSeverity.Information, "Output CRS", $"{output.Identifier} selected."));
        }

        if (input is null || output is null)
        {
            return new CrsValidationReport(findings);
        }

        // --- same system -------------------------------------------------------------------
        if (input == output)
        {
            findings.Add(new(CrsValidationSeverity.Warning, "Input vs output",
                "Input and output are the same system; coordinates will be copied without reprojection."));
        }

        // --- geographic / projected compatibility -------------------------------------------
        CrsCatalogEntry? inputEntry = await _catalog.FindAsync(input.Identifier, cancellationToken).ConfigureAwait(false);
        CrsCatalogEntry? outputEntry = await _catalog.FindAsync(output.Identifier, cancellationToken).ConfigureAwait(false);

        ValidateUnits(request, inputEntry, findings);
        ValidateCoordinateShape(request, input, findings);
        await ValidateAreaOfUseAsync(request, input, inputEntry, findings, cancellationToken).ConfigureAwait(false);

        if (inputEntry is not null && outputEntry is not null && inputEntry.IsProjected != outputEntry.IsProjected)
        {
            findings.Add(new(CrsValidationSeverity.Information, "Projection change",
                inputEntry.IsProjected
                    ? "Projected input will be converted to geographic (longitude/latitude) output."
                    : "Geographic input will be projected into a planar grid."));
        }

        // --- transformation availability -----------------------------------------------------
        // Proven by performing the transformation rather than by asking whether one exists. The
        // boolean probe cannot distinguish "PROJ knows no such transformation" from "the native
        // stack failed to load", and reporting the second as the first sent people hunting for a
        // missing EPSG definition when the real fault was a deployment problem.
        if (!request.SourceExtent.IsEmpty)
        {
            Result<Extent> transformed = _transformer.Transform(request.SourceExtent, input, output);

            if (transformed.IsFailure)
            {
                // An unavailable native stack is an environment problem, not a bad choice of
                // coordinate systems: the pair may be perfectly valid and simply cannot be
                // computed on this machine. Blocking conversion for it would stop all other work,
                // so it degrades to a warning and the drawing converts in its source coordinates.
                // A transformation PROJ genuinely does not know remains an error.
                bool environmentFault = string.Equals(
                    transformed.Error.Code, "Crs.GdalUnavailable", StringComparison.Ordinal);

                findings.Add(new(
                    environmentFault ? CrsValidationSeverity.Warning : CrsValidationSeverity.Error,
                    "Transformation",
                    environmentFault
                        ? $"Coordinate transformation is unavailable on this machine, so features "
                          + $"will be written in {input.Identifier} rather than {output.Identifier}. "
                          + $"({transformed.Error.Message})"
                        : $"{input.Identifier} → {output.Identifier} failed: {transformed.Error.Message}"));

                return new CrsValidationReport(findings);
            }

            findings.Add(new(CrsValidationSeverity.Information, "Transformation",
                $"{input.Identifier} → {output.Identifier} succeeded on the drawing's extent."));

            if (IsImplausible(transformed.Value, outputEntry))
            {
                findings.Add(new(CrsValidationSeverity.Warning, "Transformed extent",
                    "The transformed extent falls outside the output system's normal range; the input "
                    + "system may be wrong."));
            }
            else
            {
                findings.Add(new(CrsValidationSeverity.Information, "Transformed extent",
                    $"X {transformed.Value.MinX:N4}..{transformed.Value.MaxX:N4}, "
                    + $"Y {transformed.Value.MinY:N4}..{transformed.Value.MaxY:N4}."));
            }
        }
        else if (!_transformer.CanTransform(input, output))
        {
            // Same policy as above: with no geometry to test against, the cause cannot be
            // distinguished, so this does not block conversion.
            findings.Add(new(CrsValidationSeverity.Warning, "Transformation",
                $"No transformation from {input.Identifier} to {output.Identifier} is available "
                + "on this machine. If both systems are valid, the native PROJ libraries may not "
                + "have loaded; features will be written in their source coordinates."));
        }
        else
        {
            findings.Add(new(CrsValidationSeverity.Information, "Transformation",
                $"{input.Identifier} → {output.Identifier} is available."));
        }

        return new CrsValidationReport(findings);
    }

    /// <summary>Compares the drawing's declared units with the input system's units.</summary>
    private static void ValidateUnits(
        CrsValidationRequest request,
        CrsCatalogEntry? inputEntry,
        List<CrsValidationFinding> findings)
    {
        if (string.IsNullOrWhiteSpace(request.DrawingUnits) || inputEntry?.Units is not { Length: > 0 } crsUnits)
        {
            findings.Add(new(CrsValidationSeverity.Warning, "Units",
                "The drawing does not declare its units, so unit compatibility cannot be confirmed."));

            return;
        }

        bool drawingIsAngular = request.DrawingUnits.Contains("degree", StringComparison.OrdinalIgnoreCase);
        bool crsIsAngular = crsUnits.Contains("degree", StringComparison.OrdinalIgnoreCase);

        if (drawingIsAngular != crsIsAngular)
        {
            findings.Add(new(CrsValidationSeverity.Warning, "Units",
                $"The drawing is in {request.DrawingUnits} but {inputEntry.Identifier} uses {crsUnits}."));
        }
        else
        {
            findings.Add(new(CrsValidationSeverity.Information, "Units",
                $"Drawing units ({request.DrawingUnits}) are consistent with {crsUnits}."));
        }
    }

    /// <summary>Checks the coordinate magnitudes against the shape the input system implies.</summary>
    private static void ValidateCoordinateShape(
        CrsValidationRequest request,
        CoordinateSystem input,
        List<CrsValidationFinding> findings)
    {
        Extent extent = request.SourceExtent;

        if (extent.IsEmpty)
        {
            findings.Add(new(CrsValidationSeverity.Warning, "Coordinate range",
                "The drawing has no geometry, so its coordinate range cannot be checked."));

            return;
        }

        bool looksGeographic =
            extent.MinX >= -180d && extent.MaxX <= 180d && extent.MinY >= -90d && extent.MaxY <= 90d;

        if (input.IsGeographic && !looksGeographic)
        {
            findings.Add(new(CrsValidationSeverity.Error, "Coordinate range",
                $"{input.Identifier} is geographic, but the drawing's coordinates "
                + $"(X {extent.MinX:N0}..{extent.MaxX:N0}, Y {extent.MinY:N0}..{extent.MaxY:N0}) "
                + "are not longitudes and latitudes. Choose the projected system the drawing is in."));
        }
        else if (!input.IsGeographic && looksGeographic)
        {
            findings.Add(new(CrsValidationSeverity.Warning, "Coordinate range",
                "The coordinates look like longitude/latitude but a projected input system is selected."));
        }
        else if (!input.IsGeographic && IsLocalGrid(extent))
        {
            findings.Add(new(CrsValidationSeverity.Warning, "Local engineering grid",
                "Coordinates are small and near the origin, which usually means a local engineering "
                + "grid rather than a mapped projection. The output may not be georeferenced."));
        }
        else
        {
            findings.Add(new(CrsValidationSeverity.Information, "Coordinate range",
                $"X {extent.MinX:N0}..{extent.MaxX:N0}, Y {extent.MinY:N0}..{extent.MaxY:N0}."));
        }
    }

    /// <summary>Checks whether the drawing sits inside the input system's published area of use.</summary>
    private async Task ValidateAreaOfUseAsync(
        CrsValidationRequest request,
        CoordinateSystem input,
        CrsCatalogEntry? inputEntry,
        List<CrsValidationFinding> findings,
        CancellationToken cancellationToken)
    {
        if (inputEntry is null || !inputEntry.HasArea || request.SourceExtent.IsEmpty)
        {
            return;
        }

        // Compare in geographic terms: transform the drawing's centre to WGS 84 and test the box.
        Result<Extent> asWgs84 = _transformer.Transform(request.SourceExtent, input, CoordinateSystem.Wgs84);

        if (asWgs84.IsFailure)
        {
            return;
        }

        await Task.CompletedTask.ConfigureAwait(false);

        double centreLongitude = asWgs84.Value.CentreX;
        double centreLatitude = asWgs84.Value.CentreY;

        if (inputEntry.AreaContains(centreLongitude, centreLatitude))
        {
            findings.Add(new(CrsValidationSeverity.Information, "Area of use",
                $"The drawing falls inside {inputEntry.AreaName}."));
        }
        else
        {
            findings.Add(new(CrsValidationSeverity.Warning, "Area of use",
                $"The drawing centre (~{centreLongitude:F2}°, {centreLatitude:F2}°) is outside "
                + $"{inputEntry.Identifier}'s area of use ({inputEntry.AreaName}). "
                + "The input system is probably wrong."));
        }
    }

    /// <summary>Determines whether a transformed extent is outside the output system's plausible range.</summary>
    private static bool IsImplausible(Extent transformed, CrsCatalogEntry? outputEntry)
    {
        if (transformed.IsEmpty)
        {
            return true;
        }

        if (outputEntry?.CoordinateSystem.IsGeographic == true)
        {
            return transformed.MinX < -180d || transformed.MaxX > 180d
                || transformed.MinY < -90d || transformed.MaxY > 90d;
        }

        return false;
    }

    /// <summary>Determines whether an extent looks like a local engineering grid.</summary>
    private static bool IsLocalGrid(Extent extent) =>
        Math.Abs(extent.CentreX) < 100_000d && Math.Abs(extent.CentreY) < 100_000d;
}
