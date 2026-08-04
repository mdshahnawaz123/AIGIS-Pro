using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Geometry;
using AiGisConverter.Gis.Profiles;
using AiGisConverter.Gis.Tests.TestSupport;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Gis.Tests.Geometry;

public sealed class GeometryValidatorTests
{
    private static readonly GeometryFactory Factory = new();

    private static GeometryValidator Validator(Action<AiGisConverter.Gis.Options.GisOptions>? configure = null) =>
        new(GisOptionsFactory.Monitor(configure));

    private static QualityRules AllChecks() => new();

    private static Polygon Square(double x, double y, double size) =>
        Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(x, y),
            new Coordinate(x + size, y),
            new Coordinate(x + size, y + size),
            new Coordinate(x, y + size),
            new Coordinate(x, y),
        ]));

    [Fact]
    public void Validate_NullGeometry_IsReportedAsAnError()
    {
        IReadOnlyList<ValidationIssue> issues = Validator().Validate(null, "f1", AllChecks());

        issues.Should().ContainSingle();
        issues[0].Code.Should().Be("Geometry.Null");
        issues[0].Severity.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public void Validate_NullGeometry_IsSilentWhenTheCheckIsOff()
    {
        QualityRules rules = new() { CheckNullGeometry = false };

        Validator().Validate(null, "f1", rules).Should().BeEmpty();
    }

    [Fact]
    public void Validate_ValidSquare_ProducesNoFindings() =>
        Validator().Validate(Square(0d, 0d, 10d), "f1", AllChecks()).Should().BeEmpty();

    [Fact]
    public void Validate_BowtiePolygon_ReportsSelfIntersection()
    {
        Polygon bowtie = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d),
            new Coordinate(10d, 10d),
            new Coordinate(10d, 0d),
            new Coordinate(0d, 10d),
            new Coordinate(0d, 0d),
        ]));

        IReadOnlyList<ValidationIssue> issues = Validator().Validate(bowtie, "f1", AllChecks());

        issues.Should().Contain(i => i.Severity >= IssueSeverity.Error);
        issues.Should().Contain(i => i.HasLocation, "a finding without a location is not actionable");
    }

    [Fact]
    public void Validate_ZeroLengthLine_IsReported()
    {
        LineString line = Factory.CreateLineString([new Coordinate(5d, 5d), new Coordinate(5d, 5d)]);

        Validator().Validate(line, "f1", AllChecks())
            .Should().Contain(i => i.Code == "Geometry.ZeroLength");
    }

    [Fact]
    public void Validate_ZeroAreaPolygon_IsReported()
    {
        Polygon sliver = Factory.CreatePolygon(Factory.CreateLinearRing(
        [
            new Coordinate(0d, 0d),
            new Coordinate(10d, 0d),
            new Coordinate(20d, 0d),
            new Coordinate(0d, 0d),
        ]));

        Validator().Validate(sliver, "f1", AllChecks())
            .Should().Contain(i => i.Code == "Geometry.ZeroArea");
    }

    [Fact]
    public void Validate_DuplicateVertices_AreReportedOncePerGeometry()
    {
        LineString line = Factory.CreateLineString(
        [
            new Coordinate(0d, 0d),
            new Coordinate(0d, 0d),
            new Coordinate(10d, 0d),
            new Coordinate(10d, 0d),
            new Coordinate(20d, 0d),
        ]);

        IReadOnlyList<ValidationIssue> issues = Validator().Validate(line, "f1", AllChecks());

        issues.Count(i => i.Code == "Geometry.DuplicateVertices").Should().Be(1,
            "a badly exported polyline can repeat every vertex, and one finding each would bury the report");
    }

    [Fact]
    public void Validate_ClosedRing_DoesNotCountItsClosingPointAsADuplicate() =>
        Validator().Validate(Square(0d, 0d, 10d), "f1", AllChecks())
            .Should().NotContain(i => i.Code == "Geometry.DuplicateVertices");

    [Fact]
    public void Validate_EmptyGeometry_IsAWarningNotAnError()
    {
        IReadOnlyList<ValidationIssue> issues =
            Validator().Validate(Factory.CreatePolygon(), "f1", AllChecks());

        issues.Should().ContainSingle();
        issues[0].Severity.Should().Be(IssueSeverity.Warning);
    }

    [Fact]
    public void Validate_GeometryCollection_InspectsEveryMember()
    {
        GeometryCollection collection = Factory.CreateGeometryCollection(
        [
            Square(0d, 0d, 10d),
            Factory.CreateLineString([new Coordinate(1d, 1d), new Coordinate(1d, 1d)]),
        ]);

        Validator().Validate(collection, "f1", AllChecks())
            .Should().Contain(i => i.Code == "Geometry.ZeroLength");
    }

    [Fact]
    public void Validate_MinimumAreaThreshold_ComesFromConfiguration()
    {
        GeometryValidator strict = Validator(o => o.Geometry.MinimumPolygonArea = 1_000d);

        strict.Validate(Square(0d, 0d, 10d), "f1", AllChecks())
            .Should().Contain(i => i.Code == "Geometry.ZeroArea",
                "a 100 unit polygon is below a 1000 unit threshold");
    }
}
