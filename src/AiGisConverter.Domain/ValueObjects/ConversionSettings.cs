using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Validation;

namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// The settings that govern how a project's jobs are converted. Immutable.
/// </summary>
/// <remarks>
/// Held as a value object rather than as loose properties on the project so that a run can capture
/// exactly the settings it executed under. Reproducing a conversion from six months ago requires
/// knowing the threshold and target system in force at the time, not the ones in force now.
/// </remarks>
public sealed record ConversionSettings
{
    [System.Text.Json.Serialization.JsonConstructor]
    private ConversionSettings(CoordinateSystem targetCoordinateSystem, IReadOnlyList<ExportFormat> exportFormats)
    {
        TargetCoordinateSystem = targetCoordinateSystem;
        ExportFormats = exportFormats;
    }

    /// <summary>Gets the coordinate system every output is written in.</summary>
    public CoordinateSystem TargetCoordinateSystem { get; }

    /// <summary>Gets the formats to write.</summary>
    public IReadOnlyList<ExportFormat> ExportFormats { get; }

    /// <summary>Gets the system to assume when the source declares none. Null means detection must succeed.</summary>
    public CoordinateSystem? AssumedSourceCoordinateSystem { get; init; }

    /// <summary>Gets the minimum classification confidence at which a label is accepted without review.</summary>
    public double ConfidenceThreshold { get; init; } = 0.65d;

    /// <summary>Gets a value indicating whether a critical validation finding aborts the run.</summary>
    public bool StopOnCriticalIssues { get; init; } = true;

    /// <summary>Gets a value indicating whether layers hidden in the source are converted.</summary>
    public bool IncludeHiddenLayers { get; init; }

    /// <summary>Gets the linear units to assume when the source declares none.</summary>
    public LinearUnit AssumedUnits { get; init; } = LinearUnit.Unknown;

    /// <summary>
    /// Gets the closed set of feature classes a classifier may assign.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The vocabulary is a project decision, not a property of the classifier: the same drawing
    /// delivered to a utility client and to a cadastral one wants different classes. Holding it
    /// here keeps it with the rest of the settings a run captures, so a result stays explicable
    /// after the vocabulary has moved on.
    /// </para>
    /// <para>
    /// The default is a general-purpose set for site survey work. A classifier is always also
    /// offered <see cref="FeatureClass.UnclassifiedName"/>, because one with no way to decline
    /// will guess.
    /// </para>
    /// </remarks>
    public IReadOnlyList<string> CandidateFeatureClasses { get; init; } =
    [
        "Building",
        "Boundary",
        "Parcel",
        "Road",
        "Road Centreline",
        "Kerb",
        "Footpath",
        "Water Main",
        "Sewer",
        "Stormwater Pipe",
        "Manhole",
        "Electrical",
        "Telecommunication",
        "Vegetation",
        "Tree",
        "Contour",
        "Spot Level",
        "Fence",
        "Annotation",
    ];

    /// <summary>Creates settings, validating them as a whole.</summary>
    /// <param name="targetCoordinateSystem">The output coordinate system.</param>
    /// <param name="exportFormats">The formats to write. At least one is required.</param>
    /// <returns>The created settings.</returns>
    /// <exception cref="Exceptions.DomainValidationException">The settings are not usable.</exception>
    public static ConversionSettings Create(
        CoordinateSystem targetCoordinateSystem,
        IReadOnlyList<ExportFormat> exportFormats)
    {
        ArgumentNullException.ThrowIfNull(targetCoordinateSystem);
        ArgumentNullException.ThrowIfNull(exportFormats);

        new ValidationOutcome()
            .AddIf(
                exportFormats.Count == 0,
                nameof(ExportFormats),
                "At least one export format is required.",
                "Settings.NoExportFormat")
            .AddIf(
                exportFormats.Contains(ExportFormat.Unspecified),
                nameof(ExportFormats),
                "An export format must be specified.",
                "Settings.UnspecifiedExportFormat")
            .ThrowIfInvalid();

        return new ConversionSettings(targetCoordinateSystem, [.. exportFormats.Distinct()]);
    }

    /// <summary>Gets the conventional defaults: WGS 84 output as GeoJSON.</summary>
    /// <returns>The default settings.</returns>
    public static ConversionSettings Default() =>
        Create(CoordinateSystem.Wgs84, [ExportFormat.GeoJson]);

    /// <summary>Returns a copy with a different confidence threshold.</summary>
    /// <param name="threshold">The new threshold, within <c>[0, 1]</c>.</param>
    /// <returns>A new settings instance.</returns>
    public ConversionSettings WithConfidenceThreshold(double threshold)
    {
        Guard.AgainstOutOfRange(threshold, 0d, 1d);

        return this with { ConfidenceThreshold = threshold };
    }
}
