using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Application.Abstractions;

/// <summary>
/// Shared application state describing the most recently converted drawing.
/// </summary>
/// <remarks>
/// <para>
/// The conversion pipeline streams a drawing to disk and keeps nothing. The Mapping Editor needs
/// the opposite: the whole result in memory to render, filter and inspect. This session is the one
/// place that bridges the two — the pipeline publishes a snapshot when a conversion succeeds, and
/// any screen that wants to show "the current drawing" reads it here instead of holding its own
/// copy or, worse, fabricating one.
/// </para>
/// <para>
/// It is deliberately application state, not a domain concept: the domain models a conversion, not
/// "the drawing currently open in the editor". A single instance is shared across the app, so
/// loading a new drawing replaces what every consumer sees at once.
/// </para>
/// </remarks>
public interface IConversionSession
{
    /// <summary>Gets the current snapshot, or null when nothing has been converted yet.</summary>
    ConversionSessionSnapshot? Current { get; }

    /// <summary>
    /// Gets every conversion performed this run, newest first.
    /// </summary>
    /// <remarks>
    /// Kept in memory only. The value is being able to glance back at what was converted and to
    /// reopen an earlier result without re-running it, not durable audit — the summary written
    /// beside each output already serves that purpose.
    /// </remarks>
    IReadOnlyList<ConversionSessionSnapshot> History { get; }

    /// <summary>Makes an earlier conversion current again.</summary>
    /// <param name="snapshot">A snapshot previously published, taken from <see cref="History"/>.</param>
    void Reopen(ConversionSessionSnapshot snapshot);

    /// <summary>Raised on the publishing thread whenever <see cref="Current"/> changes.</summary>
    event EventHandler? Changed;

    /// <summary>Replaces the current snapshot and notifies consumers.</summary>
    /// <param name="snapshot">The snapshot to publish.</param>
    void Publish(ConversionSessionSnapshot snapshot);

    /// <summary>Clears the session, for example when the project is closed.</summary>
    void Clear();
}

/// <summary>An immutable description of one converted drawing.</summary>
/// <param name="DxfFileName">The source file name, without its path.</param>
/// <param name="DxfPath">The full source path.</param>
/// <param name="SourceCrs">The detected input coordinate system, or null when it is unknown.</param>
/// <param name="TargetCrs">The output coordinate system the features were converted into.</param>
/// <param name="SourceExtent">The drawing's extent in its own coordinates.</param>
/// <param name="TransformedExtent">The extent of the converted features, in <paramref name="TargetCrs"/>.</param>
/// <param name="Features">Every converted feature, ready to render.</param>
/// <param name="Layers">The source layers with their entity counts.</param>
/// <param name="EntityTypes">The source entity types with their counts.</param>
/// <param name="ValidationIssues">The QA/QC findings for this drawing.</param>
/// <param name="Summary">The conversion summary, describing how this result was produced.</param>
/// <param name="EntityTypeByElementId">
/// The native CAD type of each source element, keyed by element id. Carried because a converted
/// feature knows its GIS class but not the entity it came from, and rule matching and entity-type
/// selection both work in the operator's terms — <c>LWPOLYLINE</c>, not <c>Polygon</c>.
/// </param>
public sealed record ConversionSessionSnapshot(
    string DxfFileName,
    string DxfPath,
    CoordinateSystem? SourceCrs,
    CoordinateSystem TargetCrs,
    Extent SourceExtent,
    Extent TransformedExtent,
    IReadOnlyList<GisFeature> Features,
    IReadOnlyList<SessionLayer> Layers,
    IReadOnlyList<SessionEntityType> EntityTypes,
    IReadOnlyList<ValidationIssue> ValidationIssues,
    ConversionSummary Summary,
    IReadOnlyDictionary<string, string> EntityTypeByElementId)
{
    /// <summary>Gets the number of converted features.</summary>
    public int FeatureCount => Features.Count;

    /// <summary>Gets the number of source layers.</summary>
    public int LayerCount => Layers.Count;

    /// <summary>
    /// Gets a value indicating whether the output can be placed on a world basemap.
    /// </summary>
    /// <remarks>
    /// Output in WGS 84 (or any geographic system) whose coordinates fall within the valid
    /// longitude/latitude range is georeferenceable. A drawing left in local engineering
    /// coordinates is not, and must not be overlaid on satellite imagery.
    /// </remarks>
    public bool IsGeoreferenced =>
        !TransformedExtent.IsEmpty
        && TargetCrs.IsGeographic
        && TransformedExtent.MinX >= -180d && TransformedExtent.MaxX <= 180d
        && TransformedExtent.MinY >= -90d && TransformedExtent.MaxY <= 90d;
}

/// <summary>A source layer and how many entities it holds.</summary>
/// <param name="Name">The layer name.</param>
/// <param name="EntityCount">The number of entities read from the layer.</param>
public sealed record SessionLayer(string Name, int EntityCount);

/// <summary>A source entity type and how many were read.</summary>
/// <param name="Name">The native entity type, for example <c>LINE</c> or <c>LWPOLYLINE</c>.</param>
/// <param name="Count">The number read.</param>
public sealed record SessionEntityType(string Name, int Count);

/// <summary>
/// A record of how one conversion was performed, for the summary panel and the history list.
/// </summary>
/// <remarks>
/// Everything here is observed, not inferred: the CRS actually used, the extents actually produced,
/// the time actually taken. When a deliverable is questioned weeks later, this is the answer to
/// "what settings produced this file?".
/// </remarks>
/// <param name="CompletedAtUtc">When the conversion finished.</param>
/// <param name="ProjectName">The project the drawing belonged to.</param>
/// <param name="DrawingName">The drawing's file name.</param>
/// <param name="InputCrs">The input system actually used.</param>
/// <param name="OutputCrs">The output system actually used.</param>
/// <param name="CrsSource">How the input system was determined.</param>
/// <param name="DrawingUnits">The drawing's declared units.</param>
/// <param name="TransformationMethod">A description of the reprojection performed.</param>
/// <param name="TransformationConfidence">Confidence in the georeferencing, between 0 and 1.</param>
/// <param name="DetectedRegion">The region the transformed data falls in, when known.</param>
/// <param name="EntityCount">Source entities read from the drawing.</param>
/// <param name="FeatureCount">Features written.</param>
/// <param name="LayerCount">Source layers read.</param>
/// <param name="ProcessingTime">How long the conversion took.</param>
/// <param name="ExportFormats">The formats written.</param>
/// <param name="ValidationSummary">The QA/QC outcome in one line.</param>
public sealed record ConversionSummary(
    DateTimeOffset CompletedAtUtc,
    string ProjectName,
    string DrawingName,
    string InputCrs,
    string OutputCrs,
    string CrsSource,
    string DrawingUnits,
    string TransformationMethod,
    double TransformationConfidence,
    string DetectedRegion,
    int EntityCount,
    int FeatureCount,
    int LayerCount,
    TimeSpan ProcessingTime,
    string ExportFormats,
    string ValidationSummary)
{
    /// <summary>An empty summary, for a session that has not run a conversion.</summary>
    public static ConversionSummary None { get; } = new(
        DateTimeOffset.MinValue, "-", "-", "-", "-", "-", "-", "-", 0d, "-", 0, 0, 0, TimeSpan.Zero, "-", "-");

    /// <summary>Gets the confidence as a percentage string.</summary>
    public string ConfidenceText => TransformationConfidence <= 0d ? "-" : TransformationConfidence.ToString("P0");

    /// <summary>Gets the processing time in a compact human form.</summary>
    public string ProcessingTimeText => ProcessingTime == TimeSpan.Zero
        ? "-"
        : ProcessingTime.TotalSeconds < 1d
            ? $"{ProcessingTime.TotalMilliseconds:N0} ms"
            : $"{ProcessingTime.TotalSeconds:N1} s";
}
