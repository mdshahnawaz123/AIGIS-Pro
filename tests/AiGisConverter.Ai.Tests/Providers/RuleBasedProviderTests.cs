using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Providers.RuleBased;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.Enums;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Tests.Providers;

/// <summary>
/// Guards the rule-based classifier against confidently wrong answers.
/// </summary>
/// <remarks>
/// Most of these are regressions. A production export once produced a layer named
/// <c>Footpath</c> whose contents were Revit's sun path, and a layer named
/// <c>Stormwater_Pipe</c> whose contents were pipe segment definitions. Neither looked like a
/// defect downstream: the counts were plausible, the files were well formed, and the QA report was
/// clean. That is the failure mode these tests exist to prevent - not a crash, but a confident
/// answer nobody has reason to question.
/// </remarks>
public sealed class RuleBasedProviderTests
{
    private const string Unclassified = "Unclassified";

    [Fact]
    public async Task Classify_DoesNotMatchALabelThatMerelyContainsASubjectToken()
    {
        // "path" sits inside "footpath". Under the old symmetric containment rule this scored 1.0,
        // because "footpath" was the label's only token and every one of its tokens was "matched".
        ClassificationResult result = await ClassifyAsync(
            Subject("sun-path", "Sun Path"),
            "Footpath",
            Unclassified);

        result.Label.Should().Be(Unclassified);
    }

    [Theory]
    [InlineData("Sun Path", "Footpath")]
    [InlineData("Standpipe Riser", "Pipe")]
    [InlineData("Domain Boundary", "Main")]
    [InlineData("Grating", "Rating")]
    public async Task Classify_RefusesAFragmentOfALabelWord(string layerName, string label)
    {
        ClassificationResult result = await ClassifyAsync(
            Subject("subject", layerName),
            label,
            Unclassified);

        result.Label.Should().Be(Unclassified);
    }

    [Fact]
    public async Task Classify_StillMatchesAConcatenatedLayerName()
    {
        // The other direction must survive: the label's whole vocabulary is present in the name,
        // which is how CAD layers are routinely written. Fixing the false positive by banning
        // containment outright would have broken this.
        ClassificationResult result = await ClassifyAsync(
            Subject("watermain", "WATERMAIN"),
            "Water Main",
            Unclassified);

        result.Label.Should().Be("Water Main");
    }

    [Fact]
    public async Task Classify_MatchesARegularPlural()
    {
        ClassificationResult result = await ClassifyAsync(
            Subject("road", "ROAD"),
            "Roads",
            Unclassified);

        result.Label.Should().Be("Roads");
    }

    [Fact]
    public async Task Classify_MatchesAnExactToken()
    {
        ClassificationResult result = await ClassifyAsync(
            Subject("contour", "C-TOPO-CONTOUR"),
            "Contour",
            Unclassified);

        result.Label.Should().Be("Contour");
    }

    [Fact]
    public async Task Classify_LeavesAnUnmappedDeclaredCategoryUnclassified()
    {
        // The second production defect. "Pipe Segments" shares the token "pipe" with
        // "Stormwater Pipe", which scored half the label's vocabulary - comfortably above the
        // similarity floor. A declared category now settles it instead.
        ClassificationSubject subject = Subject("pipe-segments", "Pipe Segments");
        subject.SetMetadata(RuleBasedProvider.BuiltInCategoryKey, "OST_PipeSegments");

        ClassificationResult result = await ClassifyAsync(subject, "Stormwater Pipe", Unclassified);

        result.Label.Should().Be(Unclassified);
        result.Confidence.Value.Should().Be(0d);
    }

    [Fact]
    public async Task Classify_IgnoresTheNameEntirelyWhenACategoryIsDeclared()
    {
        // The name here would match "Footpath" outright, and correctly so on the evidence of the
        // name alone. The declared category is better evidence and has to win before scoring runs.
        ClassificationSubject subject = Subject("sun-path", "Footpath");
        subject.SetMetadata(RuleBasedProvider.BuiltInCategoryKey, "OST_SunPath");

        ClassificationResult result = await ClassifyAsync(subject, "Footpath", Unclassified);

        result.Label.Should().Be(Unclassified);
    }

    [Fact]
    public async Task Classify_MapsADeclaredCategoryToItsConfiguredLabel()
    {
        ClassificationSubject subject = Subject("road-1", "Anything At All");
        subject.SetMetadata(RuleBasedProvider.BuiltInCategoryKey, "OST_Roads");

        ClassificationResult result = await ClassifyAsync(
            subject,
            options => options.CategoryRules["OST_Roads"] = "Carriageway",
            "Carriageway",
            Unclassified);

        result.Label.Should().Be("Carriageway");
        result.Confidence.Value.Should().Be(0.95d);
    }

    [Fact]
    public async Task Classify_PrefersTheBuiltInCategoryOverTheDisplayName()
    {
        // The display name is localised and editable; the enum name is neither. When both are
        // present and disagree, the stable one has to win.
        ClassificationSubject subject = Subject("wall-1", "Wall");
        subject.SetMetadata(RuleBasedProvider.BuiltInCategoryKey, "OST_Walls");
        subject.SetMetadata(RuleBasedProvider.CategoryKey, "Murs");

        ClassificationResult result = await ClassifyAsync(
            subject,
            options =>
            {
                options.CategoryRules["OST_Walls"] = "Building Footprint";
                options.CategoryRules["Murs"] = "Wrong Answer";
            },
            "Building Footprint",
            "Wrong Answer",
            Unclassified);

        result.Label.Should().Be("Building Footprint");
    }

    [Fact]
    public async Task Classify_FallsBackToTheDisplayNameWhenThereIsNoBuiltInCategory()
    {
        // A user-created subcategory has no enum name, so the reader writes only the display name.
        ClassificationSubject subject = Subject("custom-1", "Anything At All");
        subject.SetMetadata(RuleBasedProvider.CategoryKey, "Site Furniture");

        ClassificationResult result = await ClassifyAsync(
            subject,
            options => options.CategoryRules["Site Furniture"] = "Street Furniture",
            "Street Furniture",
            Unclassified);

        result.Label.Should().Be("Street Furniture");
    }

    [Fact]
    public async Task Classify_AcceptsACategoryThatNamesACandidateExactly()
    {
        ClassificationSubject subject = Subject("wall-1", "Basic Wall");
        subject.SetMetadata(RuleBasedProvider.CategoryKey, "Walls");

        ClassificationResult result = await ClassifyAsync(subject, "Walls", Unclassified);

        result.Label.Should().Be("Walls");
    }

    [Fact]
    public async Task Classify_LeavesACategoryMappedOutsideTheCandidateSetUnclassified()
    {
        // A rule pointing at a feature class this conversion is not producing is a configuration
        // error. Silently choosing something else would hide it.
        ClassificationSubject subject = Subject("road-1", "Road");
        subject.SetMetadata(RuleBasedProvider.BuiltInCategoryKey, "OST_Roads");

        ClassificationResult result = await ClassifyAsync(
            subject,
            options => options.CategoryRules["OST_Roads"] = "Carriageway",
            "Contour",
            Unclassified);

        result.Label.Should().Be(Unclassified);
    }

    [Fact]
    public async Task Classify_IgnoresABlankCategory()
    {
        // A blank value must not count as "declared" - that would silence the name path without
        // putting anything in its place.
        ClassificationSubject subject = Subject("contour", "CONTOUR");
        subject.SetMetadata(RuleBasedProvider.BuiltInCategoryKey, "   ");

        ClassificationResult result = await ClassifyAsync(subject, "Contour", Unclassified);

        result.Label.Should().Be("Contour");
    }

    [Fact]
    public async Task Classify_LeavesCadSubjectsOnTheNamePath()
    {
        // CAD readers write Layer, Colour and Linetype but never Category, so the deterministic
        // gate must not engage for them.
        ClassificationSubject subject = Subject("cad-1", "C-WTR-MAIN");
        subject.SetMetadata("Linetype", "DASHED");
        subject.SetMetadata("Layer", "C-WTR-MAIN");

        ClassificationResult result = await ClassifyAsync(subject, "Water Main", Unclassified);

        result.Label.Should().Be("Water Main");
    }

    [Fact]
    public async Task Classify_AppliesAKeywordRuleAheadOfSimilarity()
    {
        ClassificationSubject subject = Subject("kerb", "KERB LINE");

        ClassificationResult result = await ClassifyAsync(
            subject,
            options => options.KeywordRules["kerb"] = "Contour",
            "Contour",
            Unclassified);

        result.Label.Should().Be("Contour");
        result.Confidence.Value.Should().Be(0.80d);
    }

    private static ClassificationSubject Subject(string id, string name)
    {
        ClassificationSubject subject = new(id, name);
        subject.SetEntityCount(1);
        subject.AddGeometry(GeometryKind.Polygon, 1);

        return subject;
    }

    private static Task<ClassificationResult> ClassifyAsync(
        ClassificationSubject subject,
        params string[] labels) =>
        ClassifyAsync(subject, static _ => { }, labels);

    private static async Task<ClassificationResult> ClassifyAsync(
        ClassificationSubject subject,
        Action<RuleBasedOptions> configure,
        params string[] labels)
    {
        RuleBasedOptions options = new();
        configure(options);

        IOptionsMonitor<RuleBasedOptions> monitor = Substitute.For<IOptionsMonitor<RuleBasedOptions>>();
        monitor.CurrentValue.Returns(options);

        RuleBasedProvider provider = new(monitor, NullLogger<RuleBasedProvider>.Instance);

        AIClassificationResponse response = await provider.ClassifyAsync(
            new AIClassificationRequest([subject], new ClassificationContext(labels)));

        return response.Results.Single();
    }
}
