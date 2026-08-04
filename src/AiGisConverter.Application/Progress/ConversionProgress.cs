namespace AiGisConverter.Application;

/// <summary>
/// Progress through one conversion.
/// </summary>
/// <remarks>
/// <para>
/// Carries both a stage and an optional fraction. Some stages know how far through they are and
/// some genuinely cannot &#8212; a DXF reader does not know how many entities a file holds until
/// it has read them &#8212; and inventing a percentage for those produces a bar that jumps
/// backwards, which is worse than one that spins.
/// </para>
/// <para>
/// A readonly record struct so reporting from a tight loop allocates nothing.
/// </para>
/// </remarks>
/// <param name="Stage">The stage currently running.</param>
/// <param name="Message">Short status message suitable for a status bar.</param>
/// <param name="StageIndex">Zero-based index of the current stage.</param>
/// <param name="StageCount">How many stages the pipeline will run.</param>
/// <param name="StageFraction">Completion within the stage, or null when indeterminate.</param>
public readonly record struct ConversionProgress(
    string Stage,
    string Message,
    int StageIndex = 0,
    int StageCount = 0,
    double? StageFraction = null)
{
    /// <summary>
    /// Gets overall completion in the closed interval <c>[0, 1]</c>, or null when unknowable.
    /// </summary>
    /// <remarks>
    /// Stages are weighted equally. They are not equally long &#8212; reading a large drawing
    /// dwarfs validating it &#8212; but a weighting tuned to one file is wrong for the next, and a
    /// bar that advances steadily and lies slightly is more use than one that is accurate and
    /// stalls.
    /// </remarks>
    public double? OverallFraction =>
        StageCount <= 0 ? null : Math.Clamp((StageIndex + (StageFraction ?? 0d)) / StageCount, 0d, 1d);

    /// <inheritdoc />
    public override string ToString() =>
        StageCount > 0 ? $"[{StageIndex + 1}/{StageCount}] {Stage}: {Message}" : $"{Stage}: {Message}";
}
