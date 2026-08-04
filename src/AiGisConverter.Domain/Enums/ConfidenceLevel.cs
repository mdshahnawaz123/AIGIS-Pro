namespace AiGisConverter.Domain.Enums;

/// <summary>
/// Defines the level of confidence for a classification result.
/// </summary>
public enum ConfidenceLevel
{
    /// <summary>Confidence is below 60, meaning the item is unclassified.</summary>
    Unclassified = 0,
    
    /// <summary>Confidence is 60-80, meaning it needs attention.</summary>
    NeedsAttention = 1,
    
    /// <summary>Confidence is 80-95, meaning it needs review.</summary>
    Review = 2,
    
    /// <summary>Confidence is 95-100, meaning it is automatically accepted.</summary>
    Automatic = 3
}
