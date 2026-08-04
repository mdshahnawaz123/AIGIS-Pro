using System.Text.Json.Serialization;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Gis.Profiles;

/// <summary>Quality expectations a profile imposes.</summary>
public sealed class QualityRules
{
    /// <summary>Gets or sets the severity at or above which a run is abandoned.</summary>
    [JsonPropertyName("failAtOrAbove")]
    public IssueSeverity FailAtOrAbove { get; set; } = IssueSeverity.Critical;

    /// <summary>Gets or sets a value indicating whether self-intersections are reported.</summary>
    [JsonPropertyName("checkSelfIntersection")]
    public bool CheckSelfIntersection { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether duplicate vertices are reported.</summary>
    [JsonPropertyName("checkDuplicateVertices")]
    public bool CheckDuplicateVertices { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether zero-length lines are reported.</summary>
    [JsonPropertyName("checkZeroLength")]
    public bool CheckZeroLength { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether zero-area polygons are reported.</summary>
    [JsonPropertyName("checkZeroArea")]
    public bool CheckZeroArea { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether unclosed or invalid rings are reported.</summary>
    [JsonPropertyName("checkRingValidity")]
    public bool CheckRingValidity { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether features with no geometry are reported.</summary>
    [JsonPropertyName("checkNullGeometry")]
    public bool CheckNullGeometry { get; set; } = true;

    /// <summary>Gets or sets the required attribute fields. A missing one is reported per feature.</summary>
    [JsonPropertyName("requiredAttributes")]
    public IList<string> RequiredAttributes { get; set; } = [];
}
