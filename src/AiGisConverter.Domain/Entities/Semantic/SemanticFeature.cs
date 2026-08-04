using System;
using System.Collections.Generic;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Domain.Entities.Semantic;

/// <summary>
/// A strongly-typed semantic object enriched from a raw source element.
/// </summary>
public sealed class SemanticFeature
{
    private readonly List<SemanticRelationship> _relationships = new();

    /// <summary>
    /// Gets the stable identifier for this feature.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// Gets the raw source element this semantic feature was built from.
    /// </summary>
    public SourceElement RawSource { get; }

    /// <summary>
    /// Gets or sets the explicit geometry of the semantic feature. 
    /// If null, it falls back to the RawSource geometry.
    /// </summary>
    public Geometry? ExplicitGeometry { get; set; }

    /// <summary>
    /// Gets the resolved geometry for this feature.
    /// </summary>
    public Geometry? Geometry => ExplicitGeometry ?? RawSource.Geometry;

    /// <summary>
    /// Gets the geometry kind.
    /// </summary>
    public GeometryKind GeometryKind => RawSource.GeometryKind;

    // --- BIM / CAD Semantic Properties ---
    
    /// <summary>Gets or sets the CAD layer name.</summary>
    public string? Layer { get; set; }
    
    /// <summary>Gets or sets the CAD block name.</summary>
    public string? Block { get; set; }
    
    /// <summary>Gets or sets the BIM category.</summary>
    public string? Category { get; set; }
    
    /// <summary>Gets or sets the BIM family.</summary>
    public string? Family { get; set; }
    
    /// <summary>Gets or sets the BIM type.</summary>
    public string? Type { get; set; }
    
    /// <summary>Gets or sets the BIM level.</summary>
    public string? Level { get; set; }
    
    /// <summary>Gets or sets the BIM storey.</summary>
    public string? Storey { get; set; }
    
    /// <summary>Gets or sets the material.</summary>
    public string? Material { get; set; }
    
    /// <summary>Gets or sets the color.</summary>
    public string? Color { get; set; }
    
    /// <summary>Gets or sets the linetype.</summary>
    public string? Linetype { get; set; }
    
    /// <summary>Gets or sets the thickness.</summary>
    public double? Thickness { get; set; }
    
    /// <summary>Gets or sets the area.</summary>
    public double? Area { get; set; }
    
    /// <summary>Gets or sets the length.</summary>
    public double? Length { get; set; }
    
    /// <summary>Gets or sets the volume.</summary>
    public double? Volume { get; set; }
    
    /// <summary>Gets or sets the elevation.</summary>
    public double? Elevation { get; set; }
    
    /// <summary>Gets or sets the rotation.</summary>
    public double? Rotation { get; set; }

    // --- Graph Relationships ---
    
    /// <summary>
    /// Gets the relationships originating from or targeting this feature.
    /// </summary>
    public IReadOnlyList<SemanticRelationship> Relationships => _relationships;

    /// <summary>
    /// Adds a relationship to this feature.
    /// </summary>
    public void AddRelationship(SemanticRelationship relationship)
    {
        ArgumentNullException.ThrowIfNull(relationship);
        _relationships.Add(relationship);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SemanticFeature"/> class.
    /// </summary>
    public SemanticFeature(string id, SourceElement rawSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(rawSource);

        Id = id;
        RawSource = rawSource;
    }

    /// <inheritdoc />
    public override string ToString() => $"[Semantic] {Category ?? "Unknown"} {Id}";
}
