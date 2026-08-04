using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.QaQc.Rules.Attribute;
using AiGisConverter.QaQc.Rules.Crs;
using AiGisConverter.QaQc.Rules.Geometry;
using AiGisConverter.QaQc.Tests.TestSupport;

namespace AiGisConverter.QaQc.Tests.Rules;

public sealed class AttributeAndCrsRuleTests
{
    [Fact]
    public void RequiredField_AbsentFromTheSchema_IsOneFindingNotOnePerFeature()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
            [.. Enumerable.Range(0, 50).Select(i =>
                QaQcTestFactory.Feature($"F{i}", QaQcTestFactory.Square(i * 20d, 0d, 10d)))],
            schema: QaQcTestFactory.Schema("NAME"));

        IReadOnlyList<ValidationIssue> findings =
            [.. new RequiredFieldRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Attributes.RequiredFields.Add("PLOT")))];

        findings.Should().ContainSingle("a schema problem is one problem, not fifty");
        findings[0].FieldName.Should().Be("PLOT");
    }

    [Fact]
    public void RequiredField_PresentButEmpty_IsOneFindingPerFeature()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d), GeometryKind.Polygon, ("PLOT", "1")),
            QaQcTestFactory.Feature("B", QaQcTestFactory.Square(20d, 0d, 10d), GeometryKind.Polygon, ("PLOT", "")),
        ], schema: QaQcTestFactory.Schema("PLOT"));

        IReadOnlyList<ValidationIssue> findings =
            [.. new RequiredFieldRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Attributes.RequiredFields.Add("PLOT")))];

        findings.Should().ContainSingle();
        findings[0].FeatureId.Should().Be("B");
    }

    [Fact]
    public void UniqueField_DuplicateIdentifier_IsReported()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d), GeometryKind.Polygon, ("PLOT", "00742")),
            QaQcTestFactory.Feature("B", QaQcTestFactory.Square(20d, 0d, 10d), GeometryKind.Polygon, ("PLOT", "00742")),
        ], schema: QaQcTestFactory.Schema("PLOT"));

        IReadOnlyList<ValidationIssue> findings =
            [.. new UniqueFieldRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Attributes.UniqueFields.Add("PLOT")))];

        findings.Should().ContainSingle();
        findings[0].Message.Should().Contain("00742").And.Contain("A").And.Contain("B");
    }

    [Fact]
    public void FormatLimit_FieldNamesTruncatingToTheSameThing_IsAnError()
    {
        // Two columns silently becoming one is data loss with no error message anywhere.
        GisAttributeSchema schema = new(
        [
            FieldDefinition.Create("PIPE_DIAMETER_MM", AttributeDataType.Text),
            FieldDefinition.Create("PIPE_DIAMETER_IN", AttributeDataType.Text),
        ]);

        GisDataset dataset = QaQcTestFactory.Dataset([], schema: schema);

        IReadOnlyList<ValidationIssue> findings =
            [.. new FormatLimitRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Attributes.MaximumFieldNameLength = 10))];

        findings.Should().Contain(f => f.Severity == IssueSeverity.Error && f.Message.Contains("merged"));
    }

    [Fact]
    public void FormatLimit_OverlongTextValue_IsAWarning()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d), GeometryKind.Polygon,
                ("NOTE", new string('x', 300)))],
            schema: QaQcTestFactory.Schema("NOTE"));

        IReadOnlyList<ValidationIssue> findings =
            [.. new FormatLimitRule().Validate(
                QaQcTestFactory.Context(dataset, o => o.Attributes.MaximumTextLength = 254))];

        findings.Should().Contain(f => f.Severity == IssueSeverity.Warning && f.FieldName == "NOTE");
    }

    [Fact]
    public void CoordinateRange_ProjectedDataLabelledAsWgs84_IsCritical()
    {
        // The classic: eastings of 528,000 are not longitudes, and nothing downstream objects.
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("A", QaQcTestFactory.Square(528_000d, 181_000d, 10d))],
            crs: CoordinateSystem.Wgs84);

        IReadOnlyList<ValidationIssue> findings =
            [.. new CoordinateRangeRule().Validate(QaQcTestFactory.Context(dataset))];

        findings.Should().NotBeEmpty();
        findings[0].Severity.Should().Be(IssueSeverity.Critical);
        findings[0].Message.Should().Contain("not longitudes and latitudes");
    }

    [Fact]
    public void CoordinateRange_GenuineGeographicData_IsClean()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("A", QaQcTestFactory.Rectangle(-0.13d, 51.5d, 0.01d, 0.01d))],
            crs: CoordinateSystem.Wgs84);

        new CoordinateRangeRule().Validate(QaQcTestFactory.Context(dataset)).Should().BeEmpty();
    }

    [Fact]
    public void CoordinateRange_ProjectedDataSittingOnTheOrigin_IsReported()
    {
        // No national grid puts real survey data within a kilometre of its false origin.
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 50d))],
            crs: CoordinateSystem.Create("EPSG", 27700));

        IReadOnlyList<ValidationIssue> findings =
            [.. new CoordinateRangeRule().Validate(QaQcTestFactory.Context(dataset))];

        findings.Should().ContainSingle();
        findings[0].Message.Should().Match(msg => msg.Contains("never georeferenced") || msg.Contains("projected origin"));
    }

    [Fact]
    public void DatasetIntegrity_EmptyDataset_IsReported()
    {
        IReadOnlyList<ValidationIssue> findings =
            [.. new DatasetIntegrityRule().Validate(QaQcTestFactory.Context(QaQcTestFactory.Dataset([])))];

        findings.Should().ContainSingle();
        findings[0].Message.Should().Contain("no features");
    }

    [Fact]
    public void DatasetIntegrity_UnclassifiedLayer_IsReported()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
            [QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d))],
            name: FeatureClass.UnclassifiedName);

        IReadOnlyList<ValidationIssue> findings =
            [.. new DatasetIntegrityRule().Validate(QaQcTestFactory.Context(dataset))];

        findings.Should().Contain(f => f.Category == IssueCategory.Classification);
    }

    [Fact]
    public void DatasetIntegrity_FeaturesWithoutGeometry_AreCounted()
    {
        GisDataset dataset = QaQcTestFactory.Dataset(
        [
            QaQcTestFactory.Feature("A", QaQcTestFactory.Square(0d, 0d, 10d)),
            QaQcTestFactory.Feature("B", null),
        ]);

        IReadOnlyList<ValidationIssue> findings =
            [.. new DatasetIntegrityRule().Validate(QaQcTestFactory.Context(dataset))];

        findings.Should().Contain(f => f.Message.Contains("no geometry"));
    }
}
