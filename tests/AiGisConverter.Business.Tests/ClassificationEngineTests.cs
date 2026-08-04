using System;
using System.Linq;
using AiGisConverter.Business.Classification;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using Xunit;

namespace AiGisConverter.Business.Tests.Classification;

public class ClassificationEngineTests
{
    private static readonly string[] BasicRoadLayers = new[] { "ROAD_CL" };
    private static readonly string[] DetailedRoadLayers = new[] { "ROAD_CL" };
    private static readonly string[] DetailedRoadColors = new[] { "1" };
    private static readonly string[] BuildingLayers = new[] { "BLDG" };

    [Fact]
    public void Evaluate_WithMultipleConditions_ScoresHigherAndPrioritizes()
    {
        // Arrange
        var engine = new ClassificationEngine();
        
        var profile = new MappingProfile
        {
            Name = "TestProfile",
            Rules =
            {
                new MappingRule
                {
                    RuleName = "BasicRoad",
                    TargetFeatureClass = "Road",
                    LayerNames = BasicRoadLayers,
                    Priority = 10
                },
                new MappingRule
                {
                    RuleName = "DetailedRoad",
                    TargetFeatureClass = "PrimaryRoad",
                    LayerNames = DetailedRoadLayers,
                    Colors = DetailedRoadColors, // Red
                    Priority = 20
                }
            }
        };

        engine.AddProfile(profile);

        var element = new SourceElement("1", GeometryKind.Line);
        element.SetAttribute("Layer", "ROAD_CL");
        element.SetAttribute("Color", "1");

        // Act
        var candidates = engine.Evaluate(element);

        // Assert
        Assert.Equal(2, candidates.Count);
        
        // DetailedRoad should be first due to higher priority
        Assert.Equal("DetailedRoad", candidates[0].RuleName);
        Assert.Equal("PrimaryRoad", candidates[0].Label);
        
        // DetailedRoad should also have higher confidence (matched more conditions)
        Assert.True(candidates[0].Confidence.Value > candidates[1].Confidence.Value);
    }
    
    [Fact]
    public void Evaluate_NoMatches_ReturnsEmpty()
    {
        var engine = new ClassificationEngine();
        
        var profile = new MappingProfile
        {
            Name = "TestProfile",
            Rules = { new MappingRule { RuleName = "Building", TargetFeatureClass = "Building", LayerNames = BuildingLayers } }
        };
        engine.AddProfile(profile);

        var element = new SourceElement("1", GeometryKind.Polygon);
        element.SetAttribute("Layer", "TREE");

        var candidates = engine.Evaluate(element);

        Assert.Empty(candidates);
    }
}
