namespace AiGisConverter.Domain.Enums;

/// <summary>
/// Spatial predicates for classification rules.
/// </summary>
public enum SpatialPredicate
{
    /// <summary>No spatial predicate.</summary>
    None = 0,
    
    /// <summary>The feature contains the other feature.</summary>
    Contains = 1,
    
    /// <summary>The feature is within the other feature.</summary>
    Within = 2,
    
    /// <summary>The feature intersects the other feature.</summary>
    Intersects = 3,
    
    /// <summary>The feature touches the other feature.</summary>
    Touches = 4,
    
    /// <summary>The feature is near the other feature.</summary>
    Near = 5,
    
    /// <summary>The feature is adjacent to the other feature.</summary>
    Adjacent = 6,
    
    /// <summary>The feature crosses the other feature.</summary>
    Crosses = 7
}
