using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AiGisConverter.Domain.Entities.Semantic;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Revit;

internal sealed class RevitSemanticProvider : ISemanticProvider
{
    public async Task<SemanticGraph> ExtractSemanticsAsync(
        IAsyncEnumerable<SourceElement> elements, 
        CancellationToken cancellationToken = default)
    {
        var graph = new SemanticGraph();

        await foreach (var element in elements.WithCancellation(cancellationToken))
        {
            var feature = new SemanticFeature(element.Id, element);

            // Revit specific mapping
            // NativeType is like "RevitWall" or "IfcWall"
            feature.Category = GetAttributeString(element, "Category") ?? element.NativeType;
            feature.Family = GetAttributeString(element, "Family");
            feature.Type = GetAttributeString(element, "Type");
            feature.Level = GetAttributeString(element, "Level");
            feature.Material = GetAttributeString(element, "Material");
            feature.Elevation = GetAttributeDouble(element, "Elevation");
            feature.Area = GetAttributeDouble(element, "Area");
            feature.Volume = GetAttributeDouble(element, "Volume");
            feature.Length = GetAttributeDouble(element, "Length");

            graph.AddFeature(feature);
        }

        // Second pass to resolve relationships
        foreach (var feature in graph.Features)
        {
            var hostId = GetAttributeString(feature.RawSource, "HostId");
            if (!string.IsNullOrEmpty(hostId) && graph.GetFeature(hostId) != null)
            {
                // The host contains/hosts this feature
                graph.GetFeature(hostId)!.AddRelationship(new SemanticRelationship(
                    SemanticRelationshipType.Hosts, hostId, feature.Id));
                
                feature.AddRelationship(new SemanticRelationship(
                    SemanticRelationshipType.BelongsTo, feature.Id, hostId));
            }
            
            var levelId = GetAttributeString(feature.RawSource, "LevelId");
            if (!string.IsNullOrEmpty(levelId) && graph.GetFeature(levelId) != null)
            {
                graph.GetFeature(levelId)!.AddRelationship(new SemanticRelationship(
                    SemanticRelationshipType.Contains, levelId, feature.Id));
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
