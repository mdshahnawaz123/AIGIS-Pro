namespace AiGisConverter.Domain.Enums;

/// <summary>
/// Defines the types of relationships that can exist between semantic features.
/// </summary>
public enum SemanticRelationshipType
{
    /// <summary>The source feature physically contains the target feature (e.g. Building contains Room).</summary>
    Contains,
    
    /// <summary>The source feature hosts the target feature (e.g. Wall hosts Door).</summary>
    Hosts,
    
    /// <summary>The source feature connects to the target feature (e.g. Pipe connects Manhole).</summary>
    Connects,
    
    /// <summary>The source feature belongs to the target feature structurally or logically (e.g. Tree belongs Parcel).</summary>
    BelongsTo,
    
    /// <summary>The source feature is spatially inside the target feature (e.g. Utility inside Corridor).</summary>
    Inside
}
