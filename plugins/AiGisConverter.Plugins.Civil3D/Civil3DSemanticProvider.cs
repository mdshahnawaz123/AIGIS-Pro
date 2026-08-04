using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AiGisConverter.Domain.Entities.Semantic;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Civil3D;

internal sealed class Civil3DSemanticProvider : ISemanticProvider
{
    public async Task<SemanticGraph> ExtractSemanticsAsync(
        IAsyncEnumerable<SourceElement> elements, 
        CancellationToken cancellationToken = default)
    {
        var graph = new SemanticGraph();

        await foreach (var element in elements.WithCancellation(cancellationToken))
        {
            var feature = new SemanticFeature(element.Id, element);

            feature.Layer = GetAttributeString(element, "Layer");
            feature.Category = element.NativeType; // e.g., "AeccDbAlignment", "AeccDbPipe"
            feature.Type = GetAttributeString(element, "Style");
            feature.Elevation = GetAttributeDouble(element, "Elevation");
            feature.Area = GetAttributeDouble(element, "Area");
            feature.Length = GetAttributeDouble(element, "Length");
            feature.Volume = GetAttributeDouble(element, "Volume");

            graph.AddFeature(feature);
        }

        // Resolve relationships
        foreach (var feature in graph.Features)
        {
            // Civil3D relationships: e.g. Pipes connect to Structures
            if (feature.Category != null && feature.Category.Contains("Pipe"))
            {
                var startNodeId = GetAttributeString(feature.RawSource, "StartNodeId");
                if (!string.IsNullOrEmpty(startNodeId) && graph.GetFeature(startNodeId) != null)
                {
                    feature.AddRelationship(new SemanticRelationship(
                        SemanticRelationshipType.Connects, feature.Id, startNodeId));
                }

                var endNodeId = GetAttributeString(feature.RawSource, "EndNodeId");
                if (!string.IsNullOrEmpty(endNodeId) && graph.GetFeature(endNodeId) != null)
                {
                    feature.AddRelationship(new SemanticRelationship(
                        SemanticRelationshipType.Connects, feature.Id, endNodeId));
                }
            }
            
            // Corridor belongs to Alignment
            if (feature.Category != null && feature.Category.Contains("Corridor"))
            {
                var alignmentId = GetAttributeString(feature.RawSource, "AlignmentId");
                if (!string.IsNullOrEmpty(alignmentId) && graph.GetFeature(alignmentId) != null)
                {
                    feature.AddRelationship(new SemanticRelationship(
                        SemanticRelationshipType.BelongsTo, feature.Id, alignmentId));
                }
            }
        }

        return graph;
    }

    private static string? GetAttributeString(SourceElement element, string key)
    {
        if (element.Attributes.TryGetValue(key, out var val) && val != null)
        {
            return val.ToString();
        }
        return null;
    }

    private static double? GetAttributeDouble(SourceElement element, string key)
    {
        if (element.Attributes.TryGetValue(key, out var val) && val is IConvertible conv)
        {
            try
            {
                return conv.ToDouble(System.Globalization.CultureInfo.InvariantCulture);
            }
            catch
            {
                // ignore cast errors
            }
        }
        return null;
    }
}
