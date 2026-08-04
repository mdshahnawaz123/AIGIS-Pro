using System.ComponentModel.DataAnnotations;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.QaQc.Options;

/// <summary>
/// QA/QC configuration, bound from the <c>QaQc</c> section.
/// </summary>
/// <remarks>
/// Every threshold a rule applies is here. A number that decides whether a surveyed deliverable is
/// rejected belongs where it can be seen and argued with, not inside a comparison.
/// </remarks>
public sealed class QaQcOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "QaQc";

    /// <summary>Gets or sets the severity at or above which a run is treated as failed.</summary>
    public IssueSeverity FailAtOrAbove { get; set; } = IssueSeverity.Critical;

    /// <summary>Gets the rule identifiers to skip.</summary>
    public IList<string> DisabledRules { get; } = [];

    /// <summary>
    /// Gets or sets the number of findings a single rule may raise before it is muted.
    /// </summary>
    /// <remarks>
    /// A dataset with a systematic fault produces one finding per feature. Ten thousand identical
    /// lines bury every other finding and make the report useless; the cap keeps the report
    /// readable and records that truncation happened.
    /// </remarks>
    [Range(1, 1_000_000)]
    public int MaximumFindingsPerRule { get; set; } = 500;

    /// <summary>Gets or sets the feature count above which cross-feature topology rules are skipped.</summary>
    /// <remarks>
    /// Topology is the only stage whose cost is superlinear in practice. Zero disables the ceiling.
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int TopologyFeatureCeiling { get; set; } = 250_000;

    /// <summary>Gets the topology settings.</summary>
    public TopologyRuleOptions Topology { get; } = new();

    /// <summary>Gets the attribute settings.</summary>
    public AttributeRuleOptions Attributes { get; } = new();

    /// <summary>Gets the reporting settings.</summary>
    public ReportOptions Reporting { get; } = new();
}

/// <summary>Thresholds for the cross-feature topology rules.</summary>
public sealed class TopologyRuleOptions
{
    /// <summary>Gets or sets a value indicating whether overlapping features are reported.</summary>
    public bool CheckOverlaps { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether unsnapped line endpoints are reported.</summary>
    public bool CheckDangles { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether sliver polygons are reported.</summary>
    public bool CheckSlivers { get; set; } = true;

    /// <summary>Gets or sets the overlap area below which an overlap is noise rather than a defect.</summary>
    public double MinimumOverlapArea { get; set; } = 1e-6d;

    /// <summary>Gets or sets the distance within which two line endpoints count as connected.</summary>
    /// <remarks>
    /// In the dataset's own units. A five-millimetre unsnapped pipe end is a dangle at 0.001 and
    /// not at 0.01, so this number decides how much digitising slop the delivery tolerates.
    /// </remarks>
    public double DangleTolerance { get; set; } = 0.001d;

    /// <summary>
    /// Gets or sets the thinness ratio below which a polygon is a sliver.
    /// </summary>
    /// <remarks>
    /// The isoperimetric quotient, <c>4&#960;A / P&#178;</c>: 1 for a circle, 0.785 for a square,
    /// approaching zero as a shape becomes a splinter. A legitimate 100&#160;m by 1&#160;m footpath
    /// scores 0.031; a digitising sliver 100&#160;m by 0.1&#160;m scores 0.0031. The default of
    /// 0.01 separates them.
    /// </remarks>
    [Range(0d, 1d)]
    public double SliverThinnessRatio { get; set; } = 0.01d;

    /// <summary>Gets or sets the area above which a thin polygon is assumed to be a real corridor.</summary>
    /// <remarks>
    /// Thinness alone would flag a long legitimate strip. Requiring the polygon to be small in
    /// absolute terms as well is what distinguishes a sliver from a road reserve.
    /// </remarks>
    public double SliverMaximumArea { get; set; } = 10d;
}

/// <summary>Thresholds for the attribute rules.</summary>
public sealed class AttributeRuleOptions
{
    /// <summary>Gets the fields every feature must carry a value for.</summary>
    public IList<string> RequiredFields { get; } = [];

    /// <summary>Gets the fields whose values must be unique within a dataset.</summary>
    public IList<string> UniqueFields { get; } = [];

    /// <summary>Gets or sets the maximum field-name length before a finding is raised. Zero disables the check.</summary>
    /// <remarks>Shapefile's DBF header caps field names at ten characters.</remarks>
    [Range(0, 255)]
    public int MaximumFieldNameLength { get; set; } = 10;

    /// <summary>Gets or sets the maximum text value length before a finding is raised. Zero disables the check.</summary>
    /// <remarks>A DBF text field cannot exceed 254 characters; longer values are truncated on write.</remarks>
    [Range(0, 65535)]
    public int MaximumTextLength { get; set; } = 254;

    /// <summary>Gets or sets the proportion of null values above which a field is reported as sparse.</summary>
    [Range(0d, 1d)]
    public double SparseFieldThreshold { get; set; } = 0.95d;
}

/// <summary>Report rendering settings.</summary>
public sealed class ReportOptions
{
    /// <summary>Gets the formats written, by key: <c>html</c>, <c>csv</c>, <c>json</c>.</summary>
    public IList<string> Formats { get; } = ["html", "csv"];

    /// <summary>Gets or sets the lowest severity included in a report.</summary>
    public IssueSeverity MinimumSeverity { get; set; } = IssueSeverity.Information;

    /// <summary>Gets or sets a value indicating whether findings are grouped by rule in the HTML report.</summary>
    public bool GroupByRule { get; set; } = true;
}
