using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Profiles;
using AiGisConverter.QaQc.Options;
using AiGisConverter.QaQc.Reporting;
using Microsoft.Extensions.Options;

namespace AiGisConverter.IntegrationTests;

/// <summary>
/// Injection and traversal defences, exercised with the payloads that would exploit them.
/// </summary>
/// <remarks>
/// Every value in a report comes from a drawing produced by someone else. Layer names, block
/// names and attribute values are all attacker-controlled in the sense that matters: nobody in
/// this pipeline authored them.
/// </remarks>
public sealed class SecurityTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("aigis-security").FullName;

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private static IOptionsMonitor<QaQcOptions> Options()
    {
        IOptionsMonitor<QaQcOptions> monitor = Substitute.For<IOptionsMonitor<QaQcOptions>>();
        monitor.CurrentValue.Returns(new QaQcOptions());

        return monitor;
    }

    private static ValidationReport ReportWith(string payload) =>
        new(new Domain.Common.ConversionRunId(Guid.NewGuid()),
            [ValidationIssue.Create(IssueSeverity.Error, IssueCategory.Attribute, "X", payload)]);

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<img src=x onerror=alert(1)>")]
    [InlineData("\"><svg/onload=alert(1)>")]
    public async Task HtmlReport_EncodesScriptPayloadsFromDrawingContent(string payload)
    {
        HtmlValidationReportWriter writer = new(Options());

        Domain.Common.Result<string> result =
            await writer.WriteAsync(ReportWith(payload), Path.Combine(_root, "xss"));

        string html = await File.ReadAllTextAsync(result.Value);

        html.Should().NotContain("<script");
        html.Should().NotContain("<img");
        html.Should().NotContain("<svg");
        html.Should().Contain("&lt;");
    }

    [Theory]
    [InlineData("value,with,commas")]
    [InlineData("value \"with\" quotes")]
    [InlineData("value\nwith\nnewlines")]
    public async Task CsvReport_QuotesPayloadsThatWouldShiftColumns(string payload)
    {
        CsvValidationReportWriter writer = new(Options());

        Domain.Common.Result<string> result =
            await writer.WriteAsync(ReportWith(payload), Path.Combine(_root, "csv"));

        string[] lines = await File.ReadAllLinesAsync(result.Value);

        // The header plus one record; an unquoted payload would split into extra lines or columns.
        lines[0].Split(',').Should().HaveCount(11);
        string body = string.Join("\n", lines.Skip(1));
        body.Should().Contain("\"");
    }

    [Fact]
    public async Task JsonReport_EscapesPayloadsAndStaysParseable()
    {
        JsonValidationReportWriter writer = new(Options());

        Domain.Common.Result<string> result = await writer.WriteAsync(
            ReportWith("\"}],\"injected\":[{\"x\":1"), Path.Combine(_root, "json"));

        using System.Text.Json.JsonDocument document =
            System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(result.Value));

        document.RootElement.TryGetProperty("injected", out _).Should().BeFalse(
            "a payload must not be able to add a top-level member");
        document.RootElement.GetProperty("findings").GetArrayLength().Should().Be(1);
    }

    [Theory]
    [InlineData(@"..\..\Windows\System32\evil")]
    [InlineData("../../../etc/passwd")]
    [InlineData("C:\\absolute\\path")]
    [InlineData("con")]
    public void NamingRules_NeutraliseTraversalSequencesInLayerNames(string hostileLayerName)
    {
        // Layer names come from drawings written by anyone and end up as file names.
        string applied = new NamingRules().Apply(hostileLayerName);

        applied.Should().NotContain("..");
        applied.Should().NotContain("/");
        applied.Should().NotContain("\\");
        applied.Should().NotContain(":");
    }

    [Fact]
    public void NamingRules_NeverProduceAnEmptyName() =>
        new NamingRules().Apply("...").Should().NotBeNullOrWhiteSpace(
            "an empty segment would make Path.Combine yield the directory itself");
}
