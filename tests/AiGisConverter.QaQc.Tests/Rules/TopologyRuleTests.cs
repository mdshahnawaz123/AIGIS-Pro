using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Rules.Topology;
using AiGisConverter.QaQc.Tests.TestSupport;

namespace AiGisConverter.QaQc.Tests.Rules;

public sealed class TopologyRuleTests
{
    // ---- overlaps ----------------------------------------------------------------------------

    [Fact]
    public void Overlaps_TwoOverlappingParcels_AreReportedOnce()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d)),
            QaQcTestFactory.Feature("B", QaQcTestFactory.Square(5d, 0d, 10d)),
        ]);

        IReadOnlyList<ValidationIssue> findings =
            [.. new OverlappingFeaturesRule().Validate(QaQcTestFactory.Context(dataset))];

        findings.Should().ContainSingle("the index yields both orderings and the pair is one defect");
        findings[0].Severity.Should().Be(IssueSeverity.Error);
        findings[0].HasLocation.Should().BeTrue();
    }

    [Fact]
    public void Overlaps_AdjacentParcels_AreNotReported()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d)),
            QaQcTestFactory.Feature("B", QaQcTestFactory.Square(10d, 0d, 10d)),
        ]);

        new OverlappingFeaturesRule().Validate(QaQcTestFactory.Context(dataset))
            .Should().BeEmpty("sharing an edge is correct, not a defect");
    }

    [Fact]
    public void Overlaps_BelowTheAreaThreshold_AreIgnored()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d)),
            QaQcTestFactory.Feature("B", QaQcTestFactory.Square(9.9999999d, 0d, 10d)),
        ]);

        new OverlappingFeaturesRule()
            .Validate(QaQcTestFactory.Context(dataset, o => o.Topology.MinimumOverlapArea = 1e-3d))
            .Should().BeEmpty();
    }

    [Fact]
    public void Overlaps_Disabled_ProducesNothing()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d)),
            QaQcTestFactory.Feature("B", QaQcTestFactory.Square(5d, 0d, 10d)),
        ]);

        new OverlappingFeaturesRule()
            .Validate(QaQcTestFactory.Context(dataset, o => o.Topology.CheckOverlaps = false))
            .Should().BeEmpty();
    }

    [Fact]
    public void Overlaps_OnALineDataset_IsSkipped()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("A", QaQcTestFactory.Line(0d, 0d, 10d, 0d), GeometryKind.Line)],
            GeometryKind.Line);

        new OverlappingFeaturesRule().Validate(QaQcTestFactory.Context(dataset)).Should().BeEmpty();
    }

    // ---- dangles -----------------------------------------------------------------------------

    [Fact]
    public void Dangles_AnUnsnappedPipeEnd_IsReported()
    {
        // Two pipes that stop 5 mm apart: the drawing looks connected, the network is not.
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("P1", QaQcTestFactory.Line(0d, 0d, 10d, 0d), GeometryKind.Line),
            QaQcTestFactory.Feature("P2", QaQcTestFactory.Line(10.005d, 0d, 20d, 0d), GeometryKind.Line),
        ], GeometryKind.Line);

        IReadOnlyList<ValidationIssue> findings =
            [.. new DanglingEndpointRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Topology.DangleTolerance = 0.001d))];

        findings.Should().HaveCount(4, "both free ends of both pipes are unconnected at 1 mm tolerance");
        findings.Should().OnlyContain(f => f.Severity == IssueSeverity.Warning);
    }

    [Fact]
    public void Dangles_ToleranceWideEnoughToBridgeTheGap_ClearsTheJunction()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("P1", QaQcTestFactory.Line(0d, 0d, 10d, 0d), GeometryKind.Line),
            QaQcTestFactory.Feature("P2", QaQcTestFactory.Line(10.005d, 0d, 20d, 0d), GeometryKind.Line),
        ], GeometryKind.Line);

        IReadOnlyList<ValidationIssue> findings =
            [.. new DanglingEndpointRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Topology.DangleTolerance = 0.01d))];

        findings.Should().HaveCount(2, "the junction now counts as connected; the two outer ends remain");
    }

    [Fact]
    public void Dangles_ProperlySnappedNetwork_ReportsOnlyTheTrueTermini()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("P1", QaQcTestFactory.Line(0d, 0d, 10d, 0d), GeometryKind.Line),
            QaQcTestFactory.Feature("P2", QaQcTestFactory.Line(10d, 0d, 20d, 0d), GeometryKind.Line),
        ], GeometryKind.Line);

        IReadOnlyList<ValidationIssue> findings =
            [.. new DanglingEndpointRule().Validate(QaQcTestFactory.Context(dataset))];

        findings.Should().HaveCount(2, "a genuine terminus is indistinguishable from a mistake");
    }

    // ---- slivers -----------------------------------------------------------------------------

    [Fact]
    public void Slivers_ADigitisingSplinter_IsReported()
    {
        // 100 x 0.1 scores a thinness of 0.0031, below the 0.01 default.
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("S", QaQcTestFactory.Rectangle(0d, 0d, 100d, 0.1d))]);

        IReadOnlyList<ValidationIssue> findings =
            [.. new SliverPolygonRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Topology.SliverMaximumArea = 20d))];

        findings.Should().ContainSingle();
        findings[0].Message.Should().Contain("sliver");
    }

    [Fact]
    public void Slivers_ALegitimateNarrowFootpath_IsNotReported()
    {
        // 100 x 1 scores 0.031, above the 0.01 default. This is the calibration that matters:
        // a threshold of 0.05 would have flagged it.
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("F", QaQcTestFactory.Rectangle(0d, 0d, 100d, 1d))]);

        new SliverPolygonRule().Validate(
            QaQcTestFactory.Context(dataset, o => o.Topology.SliverMaximumArea = 1_000d))
            .Should().BeEmpty();
    }

    [Fact]
    public void Slivers_AThinButLargePolygon_IsNotReported()
    {
        // Thinness alone would flag a road reserve. The absolute-area cap is what saves it.
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("R", QaQcTestFactory.Rectangle(0d, 0d, 10_000d, 5d))]);

        new SliverPolygonRule()
            .Validate(QaQcTestFactory.Context(dataset, o => o.Topology.SliverMaximumArea = 10d))
            .Should().BeEmpty();
    }

    [Fact]
    public void Slivers_ANormalParcel_IsNotReported() =>
        new SliverPolygonRule().Validate(QaQcTestFactory.Context(
            QaQcTestFactory.Dataset([QaQcTestFactory.Feature("P", QaQcTestFactory.Square(0d, 0d, 10d))])))
            .Should().BeEmpty("a square scores 0.785");
}
