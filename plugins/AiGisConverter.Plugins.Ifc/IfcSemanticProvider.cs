using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using AiGisConverter.Domain.Entities.Semantic;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Ifc;

internal sealed class IfcSemanticProvider : ISemanticProvider
{
    public async Task<SemanticGraph> ExtractSemanticsAsync(
        IAsyncEnumerable<SourceElement> elements, 
        CancellationToken cancellationToken = default)
    {
        var graph = new SemanticGraph();

        await foreach (var element in elements.WithCancellation(cancellationToken))
        {
            var feature = new SemanticFeature(element.Id, element);

            // IFC specific mapping
            // NativeType is like "IfcWall"
            feature.Category = element.NativeType;
            feature.Family = GetAttributeString(element, "ObjectType");
            feature.Type = GetAttributeString(element, "PredefinedType");
            feature.Level = GetAttributeString(element, "BuildingStorey");
            feature.Material = GetAttributeString(element, "Material");
            feature.Elevation = GetAttributeDouble(element, "Elevation");
            feature.Area = GetAttributeDouble(element, "Area");
            feature.Volume = GetAttributeDouble(element, "Volume");
            feature.Length = GetAttributeDouble(element, "Length");

            graph.AddFeature(feature);
        }

        // Relationship linking
        foreach (var feature in graph.Features)
        {
            // IFC typical relationships:
            // "IfcRelContainedInSpatialStructure" usually links element to IfcBuildingStorey
            var storeyId = GetAttributeString(feature.RawSource, "ContainedInStoreyId");
            if (!string.IsNullOrEmpty(storeyId) && graph.GetFeature(storeyId) != null)
            {
                graph.GetFeature(storeyId)!.AddRelationship(new SemanticRelationship(
                    SemanticRelationshipType.Contains, storeyId, feature.Id));
            }
            
            // "IfcRelVoidsElement" usually hosts doors/windows
            var hostId = GetAttributeString(feature.RawSource, "HostId");
            if (!string.IsNullOrEmpty(hostId) && graph.GetFeature(hostId) != null)
            {
                graph.GetFeature(hostId)!.AddRelationship(new SemanticRelationship(
                    SemanticRelationshipType.Hosts, hostId, feature.Id));
                    
                feature.AddRelationship(new SemanticRelationship(
                    SemanticRelationshipType.BelongsTo, feature.Id, hostId));
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
