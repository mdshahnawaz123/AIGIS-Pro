namespace AiGisConverter.Domain.Enums;

/// <summary>
/// How serious a validation finding is.
/// </summary>
/// <remarks>
/// Ordered so that comparisons work: a threshold of <see cref="Warning"/> admits
/// <see cref="Error"/> and <see cref="Critical"/> as well.
/// </remarks>
public enum IssueSeverity
{
    /// <summary>Informational. Recorded for traceability, never blocks.</summary>
    Information = 0,

    /// <summary>Probably wrong, but the output is usable.</summary>
    Warning = 1,

    /// <summary>Definitely wrong. The affected feature should not be trusted.</summary>
    Error = 2,

    /// <summary>The dataset as a whole cannot be relied on. Conversion should stop.</summary>
    Critical = 3,
}
