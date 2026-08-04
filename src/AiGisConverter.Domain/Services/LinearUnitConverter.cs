using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Exceptions;

namespace AiGisConverter.Domain.Services;

/// <summary>
/// Converts lengths between the linear units a drawing may be authored in.
/// </summary>
/// <remarks>
/// <para>
/// The international foot and the US survey foot differ by two parts per million. Over a State
/// Plane coordinate of several hundred thousand feet that is roughly a metre of error, which is
/// why they are separate members of <see cref="LinearUnit"/> and why this converter refuses to
/// treat them as interchangeable.
/// </para>
/// <para>
/// <see cref="LinearUnit.Unknown"/> and <see cref="LinearUnit.Degree"/> have no metre equivalent.
/// Degrees are angular, and their ground distance depends on latitude; converting one to metres
/// requires a projection, which is the GIS layer's work.
/// </para>
/// </remarks>
public static class LinearUnitConverter
{
    private const double UsSurveyFootInMetres = 1200d / 3937d;

    /// <summary>Gets the number of metres in one of the given unit.</summary>
    /// <param name="unit">The unit to measure.</param>
    /// <returns>The conversion factor.</returns>
    /// <exception cref="DomainException">The unit has no linear metre equivalent.</exception>
    public static double MetresPerUnit(LinearUnit unit) => unit switch
    {
        LinearUnit.Millimetre => 0.001d,
        LinearUnit.Centimetre => 0.01d,
        LinearUnit.Metre => 1d,
        LinearUnit.Kilometre => 1000d,
        LinearUnit.Inch => 0.0254d,
        LinearUnit.Foot => 0.3048d,
        LinearUnit.UsSurveyFoot => UsSurveyFootInMetres,
        LinearUnit.Yard => 0.9144d,
        LinearUnit.Mile => 1609.344d,
        LinearUnit.Degree => throw new DomainException(
            "Degrees are angular and have no fixed length in metres. Project the coordinates first."),
        _ => throw new DomainException(
            "The source did not declare its units, so lengths cannot be converted. " +
            "Set the assumed units in the conversion settings."),
    };

    /// <summary>Determines whether a unit can be converted to metres.</summary>
    /// <param name="unit">The unit to test.</param>
    /// <returns><see langword="true"/> when a fixed conversion exists.</returns>
    public static bool IsLinear(LinearUnit unit) =>
        unit is not (LinearUnit.Unknown or LinearUnit.Degree);

    /// <summary>Converts a length between units.</summary>
    /// <param name="value">The length to convert.</param>
    /// <param name="from">The unit the length is in.</param>
    /// <param name="to">The unit to convert to.</param>
    /// <returns>The converted length.</returns>
    /// <exception cref="DomainException">Either unit has no linear metre equivalent.</exception>
    public static double Convert(double value, LinearUnit from, LinearUnit to)
    {
        if (from == to)
        {
            return value;
        }

        return value * MetresPerUnit(from) / MetresPerUnit(to);
    }
}
