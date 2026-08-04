using AiGisConverter.Cad.Options;

namespace AiGisConverter.Cad.Geometry;

/// <summary>
/// Decides how finely a curve must be divided to stay within a chord tolerance.
/// </summary>
/// <remarks>
/// Separated from the tessellator itself so the rule can be tested directly, and so both the DXF
/// reader and any future DWG reader make the same decision for the same geometry.
/// </remarks>
public static class CurveTessellation
{
    /// <summary>
    /// Computes how many straight segments approximate a circular sweep within the tolerance.
    /// </summary>
    /// <remarks>
    /// The sagitta of a chord subtending angle <c>a</c> on radius <c>r</c> is
    /// <c>r(1 - cos(a/2))</c>. Setting that equal to the tolerance and solving gives the largest
    /// permissible step, <c>a = 2·acos(1 - tol/r)</c>. When the tolerance exceeds the radius the
    /// constraint is vacuous and the minimum segment count governs.
    /// </remarks>
    /// <param name="radius">The arc radius, in drawing units.</param>
    /// <param name="sweepRadians">The signed included angle.</param>
    /// <param name="options">The tessellation settings.</param>
    /// <returns>The segment count, clamped to the configured bounds.</returns>
    public static int SegmentCountFor(double radius, double sweepRadians, TessellationOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        double sweep = Math.Abs(sweepRadians);

        if (radius <= 0d || sweep <= 0d || !double.IsFinite(radius) || !double.IsFinite(sweep))
        {
            return options.MinimumSegments;
        }

        double ratio = options.ChordTolerance / radius;

        // A tolerance at or beyond the radius places no real constraint on the step.
        double maximumStep = ratio >= 1d ? Math.PI : 2d * Math.Acos(1d - ratio);

        if (maximumStep <= 0d || !double.IsFinite(maximumStep))
        {
            return options.MaximumSegments;
        }

        int required = (int)Math.Ceiling(sweep / maximumStep);

        return Math.Clamp(required, options.MinimumSegments, options.MaximumSegments);
    }

    /// <summary>Normalises an angle into the half-open interval <c>[0, 2π)</c>.</summary>
    /// <param name="radians">The angle to normalise.</param>
    /// <returns>The normalised angle.</returns>
    public static double NormaliseAngle(double radians)
    {
        double twoPi = 2d * Math.PI;
        double normalised = radians % twoPi;

        return normalised < 0d ? normalised + twoPi : normalised;
    }

    /// <summary>
    /// Computes the counter-clockwise sweep from one angle to another.
    /// </summary>
    /// <remarks>
    /// A full circle is expressed as a full turn rather than as zero: DXF stores a closed circular
    /// arc with equal start and end angles, and collapsing that to nothing would drop the entity.
    /// </remarks>
    /// <param name="startRadians">The start angle.</param>
    /// <param name="endRadians">The end angle.</param>
    /// <returns>The sweep, in the interval <c>(0, 2π]</c>.</returns>
    public static double CounterClockwiseSweep(double startRadians, double endRadians)
    {
        double sweep = NormaliseAngle(endRadians) - NormaliseAngle(startRadians);

        if (sweep <= 0d)
        {
            sweep += 2d * Math.PI;
        }

        return sweep;
    }
}
