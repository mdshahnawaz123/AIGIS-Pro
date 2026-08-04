using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Cad.Units;

/// <summary>
/// Maps the DXF <c>$INSUNITS</c> header code onto a domain linear unit.
/// </summary>
/// <remarks>
/// <para>
/// Kept as a plain integer lookup rather than a cast from a vendor enum, so the mapping is
/// verifiable against the DXF specification and shared by every provider.
/// </para>
/// <para>
/// Codes with no domain equivalent map to <see cref="LinearUnit.Unknown"/> rather than to a
/// nearest guess. A drawing authored in parsecs is not a drawing this application should silently
/// treat as metres.
/// </para>
/// </remarks>
public static class DrawingUnitsMapper
{
    /// <summary>Maps a <c>$INSUNITS</c> code.</summary>
    /// <param name="insUnits">The header code.</param>
    /// <returns>The corresponding unit, or <see cref="LinearUnit.Unknown"/>.</returns>
    public static LinearUnit FromInsUnits(int insUnits) => insUnits switch
    {
        1 => LinearUnit.Inch,
        2 => LinearUnit.Foot,
        3 => LinearUnit.Mile,
        4 => LinearUnit.Millimetre,
        5 => LinearUnit.Centimetre,
        6 => LinearUnit.Metre,
        7 => LinearUnit.Kilometre,
        10 => LinearUnit.Yard,
        21 => LinearUnit.UsSurveyFoot,
        _ => LinearUnit.Unknown,
    };

    /// <summary>Gets the display name a report should use for a unit.</summary>
    /// <param name="unit">The unit.</param>
    /// <returns>The name.</returns>
    public static string DisplayName(LinearUnit unit) => unit switch
    {
        LinearUnit.Millimetre => "millimetre",
        LinearUnit.Centimetre => "centimetre",
        LinearUnit.Metre => "metre",
        LinearUnit.Kilometre => "kilometre",
        LinearUnit.Inch => "inch",
        LinearUnit.Foot => "international foot",
        LinearUnit.UsSurveyFoot => "US survey foot",
        LinearUnit.Yard => "yard",
        LinearUnit.Mile => "mile",
        LinearUnit.Degree => "degree",
        _ => "unspecified",
    };
}
