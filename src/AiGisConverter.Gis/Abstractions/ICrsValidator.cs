using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Checks a coordinate-system choice against the drawing before a conversion is allowed to run.
/// </summary>
/// <remarks>
/// The expensive mistakes in this application are silent ones: a drawing converted under the wrong
/// zone still produces a valid-looking file, in the wrong country. These checks run before any work
/// starts, and each returns a structured finding rather than a sentence, so the view can group by
/// severity and the caller can block on errors without parsing text.
/// </remarks>
public interface ICrsValidator
{
    /// <summary>Validates an input/output pair against a drawing's extent and units.</summary>
    /// <param name="request">What the operator has chosen and what the drawing contains.</param>
    /// <param name="cancellationToken">Token used to cancel the checks.</param>
    /// <returns>The findings, in the order they were produced.</returns>
    Task<CrsValidationReport> ValidateAsync(
        CrsValidationRequest request,
        CancellationToken cancellationToken = default);
}

/// <summary>The inputs to a validation run.</summary>
/// <param name="InputCrs">The chosen input system, or null when none has been selected.</param>
/// <param name="OutputCrs">The chosen output system, or null when none has been selected.</param>
/// <param name="SourceExtent">The drawing's extent in its own coordinates.</param>
/// <param name="DrawingUnits">The drawing's declared units, when known.</param>
public sealed record CrsValidationRequest(
    CoordinateSystem? InputCrs,
    CoordinateSystem? OutputCrs,
    Extent SourceExtent,
    string? DrawingUnits);

/// <summary>How serious a finding is.</summary>
public enum CrsValidationSeverity
{
    /// <summary>Confirmation that a check passed.</summary>
    Information = 0,

    /// <summary>Something the operator should see but which does not prevent conversion.</summary>
    Warning = 1,

    /// <summary>A condition that must be fixed before conversion can run.</summary>
    Error = 2,
}

/// <summary>One structured validation finding.</summary>
/// <param name="Severity">How serious the finding is.</param>
/// <param name="Check">The check that produced it, for example <c>Area of use</c>.</param>
/// <param name="Message">What the operator needs to know.</param>
public sealed record CrsValidationFinding(CrsValidationSeverity Severity, string Check, string Message)
{
    /// <summary>Gets a leading glyph matching the severity, for compact display.</summary>
    public string Glyph => Severity switch
    {
        CrsValidationSeverity.Error => "❌",
        CrsValidationSeverity.Warning => "⚠",
        _ => "✓",
    };

    /// <summary>Gets the finding formatted for a single line.</summary>
    public string Display => $"{Glyph} {Check}: {Message}";
}

/// <summary>The outcome of a validation run.</summary>
/// <param name="Findings">Every finding produced.</param>
public sealed record CrsValidationReport(IReadOnlyList<CrsValidationFinding> Findings)
{
    /// <summary>Gets a value indicating whether any finding is an error.</summary>
    public bool HasErrors => Findings.Any(static f => f.Severity == CrsValidationSeverity.Error);

    /// <summary>Gets a value indicating whether any finding is a warning.</summary>
    public bool HasWarnings => Findings.Any(static f => f.Severity == CrsValidationSeverity.Warning);

    /// <summary>Gets a value indicating whether conversion may proceed without confirmation.</summary>
    public bool IsClean => !HasErrors && !HasWarnings;

    /// <summary>Gets a one-line summary, for the status area.</summary>
    public string Summary =>
        HasErrors ? "Conversion blocked: fix the errors below."
        : HasWarnings ? "Conversion allowed, but review the warnings below."
        : "All coordinate-system checks passed.";
}
