using System.IO;
using AiGisConverter.Business.Classification;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AiGisConverter.Business.Tests.Classification;

public class RuleProfileLoaderTests
{
    [Fact]
    public void LoadProfiles_ReadsJsonFilesAndPopulatesEngine()
    {
        // Arrange
        var testDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(testDir);
        
        var json = @"
        {
            ""ProfileId"": ""test-profile"",
            ""Name"": ""Test Profile"",
            ""Rules"": [
                {
                    ""RuleName"": ""Rule1"",
                    ""TargetFeatureClass"": ""Feature1"",
                    ""LayerNames"": [""LAYER1""]
                }
            ]
        }";
        
        File.WriteAllText(Path.Combine(testDir, "test_rules.json"), json);
        
        var engine = new ClassificationEngine();
        var loader = new RuleProfileLoader(engine, NullLogger<RuleProfileLoader>.Instance, testDir);
        
        // Act
        loader.LoadProfiles();
        
        // Assert
        // A profile was added to the engine. We can verify by evaluating an element.
        var element = new AiGisConverter.Domain.Entities.Source.SourceElement("1", AiGisConverter.Domain.Enums.GeometryKind.Point);
        element.SetAttribute("Layer", "LAYER1");
        
        var candidates = engine.Evaluate(element);
        Assert.Single(candidates);
        Assert.Equal("Feature1", candidates[0].Label);
        
        // Cleanup
        Directory.Delete(testDir, true);
    }
}
