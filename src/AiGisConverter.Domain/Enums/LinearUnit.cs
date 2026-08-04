namespace AiGisConverter.Domain.Enums;

/// <summary>Linear units a drawing may be authored in.</summary>
public enum LinearUnit
{
    /// <summary>Not stated by the source. Conversion must not assume a value.</summary>
    Unknown = 0,

    /// <summary>Millimetres.</summary>
    Millimetre = 1,

    /// <summary>Centimetres.</summary>
    Centimetre = 2,

    /// <summary>Metres.</summary>
    Metre = 3,

    /// <summary>Kilometres.</summary>
    Kilometre = 4,

    /// <summary>International inches.</summary>
    Inch = 5,

    /// <summary>International feet.</summary>
    Foot = 6,

    /// <summary>US survey feet. Distinct from <see cref="Foot"/>; confusing them shifts state-plane data by metres.</summary>
    UsSurveyFoot = 7,

    /// <summary>International yards.</summary>
    Yard = 8,

    /// <summary>Statute miles.</summary>
    Mile = 9,

    /// <summary>Degrees, for geographic coordinates.</summary>
    Degree = 10,
}
