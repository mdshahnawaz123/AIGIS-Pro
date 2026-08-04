using System;
using System.Collections.Generic;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Business.Classification;

/// <summary>
/// Represents a mapping profile loaded from external JSON files.
/// </summary>
public sealed class MappingProfile
{
    /// <summary>Gets or sets the unique identifier for the profile.</summary>
    public string ProfileId { get; set; } = string.Empty;

    /// <summary>Gets or sets the name of the mapping profile.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the version of the profile.</summary>
    public string Version { get; set; } = "1.0";

    /// <summary>Gets or sets the author of the profile.</summary>
    public string Author { get; set; } = string.Empty;

    /// <summary>Gets or sets the supported Coordinate Reference System (CRS) for this profile.</summary>
    public string? SupportedCRS { get; set; }

    /// <summary>Gets or sets the rules contained in this profile.</summary>
    public List<MappingRule> Rules { get; set; } = new();
}

/// <summary>
/// Represents a single mapping rule within a profile.
/// </summary>
public sealed class MappingRule
{
    /// <summary>Gets or sets the name of the rule.</summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>Gets or sets the priority of the rule. Higher number indicates higher priority.</summary>
    public int Priority { get; set; }

    /// <summary>Gets or sets the target feature class to assign when the rule matches.</summary>
    public string TargetFeatureClass { get; set; } = string.Empty;

    /// <summary>Gets or sets the target feature class to assign when the rule matches. (Backwards compatibility)</summary>
    [Obsolete("Use TargetFeatureClass instead.")]
    public string FeatureClass { get => TargetFeatureClass; set => TargetFeatureClass = value; }

    /// <summary>Gets or sets the layer names.</summary>
    public string[]? LayerNames { get; set; }

    /// <summary>Gets or sets the entity types.</summary>
    public string[]? EntityTypes { get; set; }

    /// <summary>Gets or sets the geometry types.</summary>
    public string[]? GeometryTypes { get; set; }

    /// <summary>Gets or sets the block names.</summary>
    public string[]? BlockNames { get; set; }

    /// <summary>Gets or sets the block attributes.</summary>
    public Dictionary<string, string>? BlockAttributes { get; set; }

    /// <summary>Gets or sets the text pattern.</summary>
    public string? TextPattern { get; set; }

    /// <summary>Gets or sets the colors.</summary>
    public string[]? Colors { get; set; }

    /// <summary>Gets or sets the line types.</summary>
    public string[]? LineTypes { get; set; }

    /// <summary>Gets or sets the extended data strings.</summary>
    public string[]? XData { get; set; }

    /// <summary>Gets or sets the attributes.</summary>
    public Dictionary<string, string>? Attributes { get; set; }

    /// <summary>Gets or sets the minimum area.</summary>
    public double? MinimumArea { get; set; }

    /// <summary>Gets or sets the maximum area.</summary>
    public double? MaximumArea { get; set; }

    /// <summary>Gets or sets the minimum length.</summary>
    public double? MinimumLength { get; set; }

    /// <summary>Gets or sets the maximum length.</summary>
    public double? MaximumLength { get; set; }

    /// <summary>Gets or sets the required Coordinate Reference System.</summary>
    public string? RequiredCRS { get; set; }

    /// <summary>Gets or sets the spatial predicate.</summary>
    public SpatialPredicate? SpatialPredicate { get; set; }

    /// <summary>Legacy conditions for backwards compatibility.</summary>
    public Dictionary<string, string>? Conditions { get; set; }
}
