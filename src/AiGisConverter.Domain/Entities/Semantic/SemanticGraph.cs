using System;
using System.Collections.Generic;
using System.Linq;

namespace AiGisConverter.Domain.Entities.Semantic;

/// <summary>
/// A graph representing a collection of semantic features and their relationships.
/// </summary>
public sealed class SemanticGraph
{
    private readonly Dictionary<string, SemanticFeature> _features = new();
    
    /// <summary>
    /// Gets all semantic features in the graph.
    /// </summary>
    public IReadOnlyCollection<SemanticFeature> Features => _features.Values;

    /// <summary>
    /// Adds a feature to the graph.
    /// </summary>
    public void AddFeature(SemanticFeature feature)
    {
        ArgumentNullException.ThrowIfNull(feature);
        _features[feature.Id] = feature;
    }

    /// <summary>
    /// Gets a feature by its identifier.
    /// </summary>
    public SemanticFeature? GetFeature(string id)
    {
        _features.TryGetValue(id, out var feature);
        return feature;
    }

    /// <summary>
    /// Resolves all target features for a given relationship type from a source feature.
    /// </summary>
    public IEnumerable<SemanticFeature> GetTargets(string sourceFeatureId, Enums.SemanticRelationshipType type)
    {
        if (!_features.TryGetValue(sourceFeatureId, out var source))
        {
            yield break;
        }

        foreach (var rel in source.Relationships.Where(r => r.RelationshipType == type && r.SourceFeatureId == sourceFeatureId))
        {
            if (_features.TryGetValue(rel.TargetFeatureId, out var target))
            {
                yield return target;
            }
        }
    }

    /// <summary>
    /// Resolves all source features for a given relationship type that point to a target feature.
    /// </summary>
    public IEnumerable<SemanticFeature> GetSources(string targetFeatureId, Enums.SemanticRelationshipType type)
    {
        // For performance in large graphs, a reverse-index of relationships could be maintained.
        // For now, we query the features.
        foreach (var feature in _features.Values)
        {
            if (feature.Relationships.Any(r => r.RelationshipType == type && r.TargetFeatureId == targetFeatureId))
            {
                yield return feature;
            }
        }
    }
}
