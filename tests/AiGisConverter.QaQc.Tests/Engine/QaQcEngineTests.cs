using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Abstractions;
using AiGisConverter.QaQc.Engine;
using AiGisConverter.QaQc.Options;
using AiGisConverter.QaQc.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.QaQc.Tests.Engine;

public sealed class QaQcEngineTests
{
    private static QaQcEngine Create(
        IEnumerable<IValidationRule> rules,
        Action<QaQcOptions>? configure = null) =>
        new([new BuiltInValidationRuleSource(rules)],
            QaQcTestFactory.Monitor(configure),
            NullLogger<QaQcEngine>.Instance);

    private static GisDataset Dataset(int features = 3) =>
        QaQcTestFactory.Dataset(
            [.. Enumerable.Range(0, features).Select(i =>
                QaQcTestFactory.Feature($"F{i}", QaQcTestFactory.Square(i * 20d, 0d, 10d)))]);

    [Fact]
    public async Task ValidateAsync_NoRules_FailsRatherThanClaimingACleanDataset()
    {
        Result<ValidationReport> result =
            await Create([]).ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset()]);

        result.IsFailure.Should().BeTrue("an empty rule set means nothing was checked, not that all is well");
    }

    [Fact]
    public async Task ValidateAsync_CleanDataset_ProducesAnAcceptableReport()
    {
        Result<ValidationReport> result = await Create([new StubRule("R1", [])])
            .ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.TotalCount.Should().Be(0);
        result.Value.IsAcceptable(IssueSeverity.Critical).Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_AThrowingRule_DoesNotCostTheOtherRulesTheirFindings()
    {
        StubRule good = new("Good", [Issue(IssueSeverity.Warning, "Good")]);

        Result<ValidationReport> result = await Create([new ThrowingRule(), good])
            .ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset()]);

        result.IsSuccess.Should().BeTrue();
        result.Value.Issues.Should().Contain(i => i.Code == "Good");
        result.Value.Issues.Should().Contain(i => i.Code == "QaQc.RuleFailed",
            "the broken rule is reported rather than hidden");
    }

    [Fact]
    public async Task ValidateAsync_RuleExceedingTheCap_IsTruncatedVisibly()
    {
        StubRule noisy = new("Noisy", [.. Enumerable.Range(0, 100).Select(i => Issue(IssueSeverity.Warning, "Noisy"))]);

        Result<ValidationReport> result = await Create([noisy], o => o.MaximumFindingsPerRule = 10)
            .ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset()]);

        result.Value.Issues.Count(i => i.Code == "Noisy").Should().Be(10);
        result.Value.Issues.Should().Contain(i => i.Code == "QaQc.FindingsTruncated",
            "silent truncation would make the report a lie");
    }

    [Fact]
    public async Task ValidateAsync_DisabledRule_IsNotRun()
    {
        StubRule rule = new("Disabled.Me", [Issue(IssueSeverity.Error, "Disabled.Me")]);

        Result<ValidationReport> result = await Create([rule], o => o.DisabledRules.Add("Disabled.Me"))
            .ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset()]);

        result.IsFailure.Should().BeTrue("disabling the only rule leaves nothing to check with");
    }

    [Fact]
    public async Task ValidateAsync_DatasetAboveTheCeiling_SkipsWholeDatasetRulesAndSaysSo()
    {
        StubRule crossFeature = new("Cross", [Issue(IssueSeverity.Error, "Cross")]) { WholeDataset = true };
        StubRule perFeature = new("Per", [Issue(IssueSeverity.Warning, "Per")]);

        Result<ValidationReport> result = await Create([crossFeature, perFeature], o => o.TopologyFeatureCeiling = 2)
            .ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset(features: 5)]);

        result.Value.Issues.Should().NotContain(i => i.Code == "Cross");
        result.Value.Issues.Should().Contain(i => i.Code == "Per");
        result.Value.Issues.Should().Contain(i => i.Code == "QaQc.TopologySkipped");
    }

    [Fact]
    public async Task ValidateAsync_DuplicateRuleIds_KeepTheFirst()
    {
        StubRule first = new("Same", [Issue(IssueSeverity.Warning, "Same")]);
        StubRule second = new("Same", [Issue(IssueSeverity.Error, "Same"), Issue(IssueSeverity.Error, "Same")]);

        Result<ValidationReport> result = await Create([first, second])
            .ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset()]);

        result.Value.Issues.Count(i => i.Code == "Same").Should().Be(1);
    }

    [Fact]
    public async Task ValidateAsync_Cancellation_Propagates()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = async () => await Create([new StubRule("R", [])])
            .ValidateAsync(new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset()], null, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ValidateAsync_ReportsProgressPerDataset()
    {
        List<string> reported = [];
        Progress<string> progress = new(reported.Add);

        await Create([new StubRule("R", [])]).ValidateAsync(
            new Domain.Common.ConversionRunId(Guid.NewGuid()), [Dataset(), Dataset()], progress);

        await Task.Delay(50);

        reported.Should().NotBeEmpty();
    }

    private static ValidationIssue Issue(IssueSeverity severity, string code) =>
        ValidationIssue.Create(severity, IssueCategory.Topology, code, "stub finding");

    private sealed class StubRule : IValidationRule
    {
        private readonly IReadOnlyList<ValidationIssue> _issues;

        public StubRule(string ruleId, IReadOnlyList<ValidationIssue> issues)
        {
            RuleId = ruleId;
            _issues = issues;
        }

        public string RuleId { get; }

        public string DisplayName => RuleId;

        public IssueCategory Category => IssueCategory.Topology;

        public bool WholeDataset { get; init; }

        public bool RequiresWholeDataset => WholeDataset;

        public IEnumerable<ValidationIssue> Validate(
            ValidationContext context,
            CancellationToken cancellationToken = default) => _issues;
    }

    private sealed class ThrowingRule : IValidationRule
    {
        public string RuleId => "Broken";

        public string DisplayName => "Broken rule";

        public IssueCategory Category => IssueCategory.Geometry;

        public bool RequiresWholeDataset => false;

        public IEnumerable<ValidationIssue> Validate(
            ValidationContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("deliberate rule failure");
    }
}
