namespace AiGisConverter.Cad.Geometry;

/// <summary>
/// The circular arc implied by a DXF bulge value between two polyline vertices.
/// </summary>
/// <param name="CentreX">Arc centre X.</param>
/// <param name="CentreY">Arc centre Y.</param>
/// <param name="Radius">Arc radius, always positive.</param>
/// <param name="StartAngle">Angle from the centre to the first vertex, in radians.</param>
/// <param name="Sweep">Signed included angle. Positive is counter-clockwise.</param>
public readonly record struct BulgeArc(
    double CentreX,
    double CentreY,
    double Radius,
    double StartAngle,
    double Sweep)
{
    /// <summary>
    /// Below this magnitude a bulge is treated as a straight segment.
    /// </summary>
    /// <remarks>
    /// A bulge of 1e-9 describes an arc whose sagitta over a one-kilometre chord is half a
    /// micron. Treating it as curved gains nothing and risks a division that produces a radius of
    /// several billion, which then defeats every downstream tolerance check.
    /// </remarks>
    public const double StraightThreshold = 1e-9d;

    /// <summary>
    /// Derives the arc joining two vertices for a given bulge.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A DXF bulge is <c>tan(θ/4)</c>, where <c>θ</c> is the included angle and a positive value
    /// means the arc runs counter-clockwise from the first vertex to the second. From that,
    /// the sagitta is <c>s = b·c/2</c> for chord length <c>c</c>, and the radius follows from
    /// <c>r = (c²/4 + s²) / 2s</c>. The centre sits on the chord's perpendicular bisector at a
    /// signed distance <c>r − s</c> from the midpoint, on the left of the chord direction.
    /// </para>
    /// <para>
    /// Working from the sagitta rather than from <c>r = c / (2·sin(θ/2))</c> avoids the
    /// singularity at <c>θ → 0</c>, which is exactly the case that arises most often in real
    /// drawings: polylines whose bulges are almost, but not quite, zero.
    /// </para>
    /// </remarks>
    /// <param name="startX">First vertex X.</param>
    /// <param name="startY">First vertex Y.</param>
    /// <param name="endX">Second vertex X.</param>
    /// <param name="endY">Second vertex Y.</param>
    /// <param name="bulge">The bulge value stored on the first vertex.</param>
    /// <param name="arc">The derived arc, when the segment is curved.</param>
    /// <returns><see langword="false"/> when the segment is straight or degenerate.</returns>
    public static bool TryCreate(
        double startX,
        double startY,
        double endX,
        double endY,
        double bulge,
        out BulgeArc arc)
    {
        arc = default;

        if (!double.IsFinite(bulge) || Math.Abs(bulge) < StraightThreshold)
        {
            return false;
        }

        double dx = endX - startX;
        double dy = endY - startY;
        double chord = Math.Sqrt((dx * dx) + (dy * dy));

        if (chord <= 0d || !double.IsFinite(chord))
        {
            return false;
        }

        double sagitta = bulge * chord / 2d;
        double radius = (((chord * chord) / 4d) + (sagitta * sagitta)) / (2d * sagitta);

        if (!double.IsFinite(radius) || radius == 0d)
        {
            return false;
        }

        // Left-hand normal of the chord direction.
        double normalX = -dy / chord;
        double normalY = dx / chord;

        double midX = (startX + endX) / 2d;
        double midY = (startY + endY) / 2d;

        double offset = radius - sagitta;
        double centreX = midX + (normalX * offset);
        double centreY = midY + (normalY * offset);

        double startAngle = Math.Atan2(startY - centreY, startX - centreX);

        arc = new BulgeArc(
            centreX,
            centreY,
            Math.Abs(radius),
            startAngle,
            4d * Math.Atan(bulge));

        return true;
    }
}
