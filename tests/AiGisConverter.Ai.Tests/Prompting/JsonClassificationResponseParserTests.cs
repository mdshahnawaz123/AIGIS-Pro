using AiGisConverter.Ai.Prompting;
using AiGisConverter.Ai.Tests.TestSupport;
using AiGisConverter.Domain.Entities.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Ai.Tests.Prompting;

/// <summary>
/// The parser is the layer's blast door: everything a language model can get wrong arrives here.
/// </summary>
public sealed class JsonClassificationResponseParserTests
{
    private static readonly JsonClassificationResponseParser Parser =
        new(NullLogger<JsonClassificationResponseParser>.Instance);

    private static IReadOnlyList<ClassificationResult> Parse(string content) =>
        Parser.Parse(content, [AiTestFactory.Subject("L1")], AiTestFactory.Context(), "fake");

    [Fact]
    public void Parse_CleanJson_Succeeds()
    {
        IReadOnlyList<ClassificationResult> results = Parse(
            """{"results":[{"id":"L1","label":"Water Main","confidence":0.91,"rationale":"WTR prefix"}]}""");

        results.Should().ContainSingle();
        results[0].Label.Should().Be("Water Main");
        results[0].Confidence.Value.Should().BeApproximately(0.91d, 1e-9d);
        results[0].Rationale.Should().Be("WTR prefix");
    }

    [Fact]
    public void Parse_MarkdownFencedJson_Succeeds()
    {
        // Models wrap JSON in a fence constantly, whatever the system prompt says.
        IReadOnlyList<ClassificationResult> results = Parse(
            "```json\n{\"results\":[{\"id\":\"L1\",\"label\":\"Water Main\",\"confidence\":0.8}]}\n```");

        results.Should().ContainSingle();
        results[0].Label.Should().Be("Water Main");
    }

    [Fact]
    public void Parse_JsonWithProsePreamble_Succeeds()
    {
        IReadOnlyList<ClassificationResult> results = Parse(
            "Certainly! Here is the classification you asked for:\n" +
            "{\"results\":[{\"id\":\"L1\",\"label\":\"Water Main\",\"confidence\":0.7}]}\n" +
            "Let me know if you need anything else.");

        results.Should().ContainSingle();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("I am unable to help with that.")]
    [InlineData("{not json at all")]
    [InlineData("{\"results\": \"a string, not an array\"}")]
    [InlineData("{\"other\":[]}")]
    public void Parse_UnusableContent_ReturnsEmptyRatherThanThrowing(string content)
    {
        Func<IReadOnlyList<ClassificationResult>> act = () => Parse(content);

        act.Should().NotThrow();
        act().Should().BeEmpty();
    }

    [Fact]
    public void Parse_PercentageConfidence_IsNormalised()
    {
        // Asked for 0.85, models routinely answer 85.
        Parse("""{"results":[{"id":"L1","label":"Water Main","confidence":85}]}""")[0]
            .Confidence.Value.Should().BeApproximately(0.85d, 1e-9d);
    }

    [Fact]
    public void Parse_StringConfidence_IsAccepted() =>
        Parse("""{"results":[{"id":"L1","label":"Water Main","confidence":"0.62"}]}""")[0]
            .Confidence.Value.Should().BeApproximately(0.62d, 1e-9d);

    [Theory]
    [InlineData(-5)]
    [InlineData(1000)]
    public void Parse_OutOfRangeConfidence_IsClamped(double confidence)
    {
        double value = Parse($$"""{"results":[{"id":"L1","label":"Water Main","confidence":{{confidence}}}]}""")[0]
            .Confidence.Value;

        value.Should().BeInRange(0d, 1d);
    }

    [Fact]
    public void Parse_LabelOutsideTheCandidateSet_BecomesUnclassified() =>
        Parse("""{"results":[{"id":"L1","label":"Something Invented","confidence":0.99}]}""")[0]
            .Label.Should().Be("Unclassified", "a model must not be able to invent a feature class");

    [Fact]
    public void Parse_LabelCasingIsTolerated() =>
        Parse("""{"results":[{"id":"L1","label":"water main","confidence":0.9}]}""")[0]
            .Label.Should().Be("water main", "the label matched the candidate set case-insensitively");

    [Fact]
    public void Parse_UnknownSubjectId_IsDiscarded() =>
        Parse("""{"results":[{"id":"NOT-A-LAYER","label":"Water Main","confidence":0.9}]}""")
            .Should().BeEmpty();

    [Fact]
    public void Parse_MissingConfidence_DefaultsToZero() =>
        Parse("""{"results":[{"id":"L1","label":"Water Main"}]}""")[0]
            .Confidence.Value.Should().Be(0d);

    [Fact]
    public void Parse_Alternatives_AreKeptOnlyWhenInTheCandidateSet()
    {
        ClassificationResult result = Parse(
            """
            {"results":[{"id":"L1","label":"Water Main","confidence":0.6,
             "alternatives":[{"label":"Stormwater Pipe","confidence":0.3},
                             {"label":"Invented","confidence":0.9}]}]}
            """)[0];

        result.Alternatives.Should().ContainSingle();
        result.Alternatives[0].Label.Should().Be("Stormwater Pipe");
    }

    [Fact]
    public void Parse_PartialAnswer_ReturnsOnlyWhatWasAnswered()
    {
        IReadOnlyList<ClassificationResult> results = Parser.Parse(
            """{"results":[{"id":"L1","label":"Water Main","confidence":0.8}]}""",
            [AiTestFactory.Subject("L1"), AiTestFactory.Subject("L2", "C-STRM")],
            AiTestFactory.Context(),
            "fake");

        results.Should().ContainSingle("the service fills the gap, not the parser");
    }

    [Fact]
    public void Parse_TrailingCommasAndComments_AreTolerated() =>
        Parse("""
              {
                // a model that thinks JSON has comments
                "results":[{"id":"L1","label":"Water Main","confidence":0.5,}],
              }
              """).Should().ContainSingle();
}
