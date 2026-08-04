using System.Linq;
using System.Text.Json;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.QaQc.Reporting;
using AiGisConverter.QaQc.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.QaQc.Tests.Reporting;

public sealed class ReportWriterTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aigis-qaqc-tests").FullName;

    private static ValidationReport Report() => new(
        new ConversionRunId(Guid.NewGuid()),
        [
            ValidationIssue.Create(IssueSeverity.Critical, IssueCategory.Crs, "Crs.OutOfRange",
                "Coordinates are not longitudes").At(528000d, 181000d),
            ValidationIssue.Create(IssueSeverity.Error, IssueCategory.Topology, "Topology.Overlaps",
                "A overlaps B, by 4 units").ForFeature("A").WithRemediation("Snap the boundary"),
            ValidationIssue.Create(IssueSeverity.Warning, IssueCategory.Attribute, "Attribute.Sparse",
                "Mostly empty").ForField("NOTE"),
        ]);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A writer still holding a handle must not fail a test that already passed.
        }
    }

    [Fact]
    public async Task Html_IsSelfContained()
    {
        HtmlValidationReportWriter writer = new(QaQcTestFactory.Monitor());

        Result<string> result = await writer.WriteAsync(Report(), Path.Combine(_root, "report"));

        string html = await File.ReadAllTextAsync(result.Value);

        html.Should().StartWith("<!DOCTYPE html>");
        html.Should().NotContain("<link", "a QA report is opened from a network share, offline");
        html.Should().NotContain("src=\"http", "nothing it has to fetch is a thing it will fail to fetch");
    }

    [Fact]
    public async Task Html_EncodesValuesFromTheDrawing()
    {
        // Layer names come from CAD files written by anyone.
        ValidationReport report = new(
            new ConversionRunId(Guid.NewGuid()),
            [ValidationIssue.Create(IssueSeverity.Error, IssueCategory.Attribute, "X",
                "<script>alert('layer')</script>")]);

        HtmlValidationReportWriter writer = new(QaQcTestFactory.Monitor());
        Result<string> result = await writer.WriteAsync(report, Path.Combine(_root, "xss"));

        string html = await File.ReadAllTextAsync(result.Value);

        html.Should().NotContain("<script>alert");
        html.Should().Contain("&lt;script&gt;");
    }

    [Fact]
    public async Task Html_ShowsTheVerdictAgainstTheConfiguredThreshold()
    {
        HtmlValidationReportWriter writer = new(
            QaQcTestFactory.Monitor(o => o.FailAtOrAbove = IssueSeverity.Error));

        Result<string> result = await writer.WriteAsync(Report(), Path.Combine(_root, "verdict"));

        (await File.ReadAllTextAsync(result.Value)).Should().Contain("Rejected");
    }

    [Fact]
    public async Task Csv_QuotesValuesContainingCommas()
    {
        CsvValidationReportWriter writer = new(QaQcTestFactory.Monitor());

        Result<string> result = await writer.WriteAsync(Report(), Path.Combine(_root, "report"));

        string[] lines = await File.ReadAllLinesAsync(result.Value);

        lines[0].Should().StartWith("severity,category,code,message");
        lines.Should().HaveCount(4, "one header plus three findings");
        lines.Should().Contain(l => l.Contains("\"A overlaps B, by 4 units\""));
    }

    [Fact]
    public async Task Csv_WritesABomSoExcelReadsUtf8()
    {
        CsvValidationReportWriter writer = new(QaQcTestFactory.Monitor());

        Result<string> result = await writer.WriteAsync(Report(), Path.Combine(_root, "bom"));

        (await File.ReadAllBytesAsync(result.Value))[..3].Should().Equal([0xEF, 0xBB, 0xBF]);
    }

    [Fact]
    public async Task Csv_HonoursTheMinimumSeverity()
    {
        CsvValidationReportWriter writer = new(
            QaQcTestFactory.Monitor(o => o.Reporting.MinimumSeverity = IssueSeverity.Error));

        Result<string> result = await writer.WriteAsync(Report(), Path.Combine(_root, "filtered"));

        (await File.ReadAllLinesAsync(result.Value)).Should().HaveCount(3, "the warning is excluded");
    }

    [Fact]
    public async Task Json_SummaryCarriesTheVerdictWithoutParsingTheFindings()
    {
        JsonValidationReportWriter writer = new(
            QaQcTestFactory.Monitor(o => o.FailAtOrAbove = IssueSeverity.Critical));

        Result<string> result = await writer.WriteAsync(Report(), Path.Combine(_root, "report"));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(result.Value));
        JsonElement summary = document.RootElement.GetProperty("summary");

        summary.GetProperty("total").GetInt32().Should().Be(3);
        summary.GetProperty("highestSeverity").GetString().Should().Be("Critical");
        summary.GetProperty("acceptable").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task Json_FindingCarriesLocationAndRemediationWhenPresent()
    {
        JsonValidationReportWriter writer = new(QaQcTestFactory.Monitor());

        Result<string> result = await writer.WriteAsync(Report(), Path.Combine(_root, "detail"));

        using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(result.Value));
        JsonElement findings = document.RootElement.GetProperty("findings");

        findings.GetArrayLength().Should().Be(3);
        findings.EnumerateArray().Any(f => f.TryGetProperty("location", out _)).Should().BeTrue();
        findings.EnumerateArray().Any(f => f.TryGetProperty("remediation", out _)).Should().BeTrue();
    }

    [Fact]
    public async Task Renderer_WritesEveryConfiguredFormat()
    {
        ValidationReportRenderer renderer = new(
            [new HtmlValidationReportWriter(QaQcTestFactory.Monitor()),
             new CsvValidationReportWriter(QaQcTestFactory.Monitor()),
             new JsonValidationReportWriter(QaQcTestFactory.Monitor())],
            QaQcTestFactory.Monitor(o =>
            {
                o.Reporting.Formats.Clear();
                o.Reporting.Formats.Add("html");
                o.Reporting.Formats.Add("csv");
                o.Reporting.Formats.Add("json");
            }),
            NullLogger<ValidationReportRenderer>.Instance);

        IReadOnlyList<string> written = await renderer.RenderAsync(Report(), Path.Combine(_root, "all"));

        written.Should().HaveCount(3);
        written.Should().OnlyContain(p => File.Exists(p));
    }

    [Fact]
    public async Task Renderer_UnknownFormat_IsSkippedWithoutLosingTheOthers()
    {
        ValidationReportRenderer renderer = new(
            [new CsvValidationReportWriter(QaQcTestFactory.Monitor())],
            QaQcTestFactory.Monitor(o =>
            {
                o.Reporting.Formats.Clear();
                o.Reporting.Formats.Add("pdf");
                o.Reporting.Formats.Add("csv");
            }),
            NullLogger<ValidationReportRenderer>.Instance);

        IReadOnlyList<string> written = await renderer.RenderAsync(Report(), Path.Combine(_root, "partial"));

        written.Should().ContainSingle("a missing writer must not cost the formats that do exist");
    }
}
