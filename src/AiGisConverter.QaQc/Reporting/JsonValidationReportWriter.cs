using System.Text.Json;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Options;
using Microsoft.Extensions.Options;

namespace AiGisConverter.QaQc.Reporting;

/// <summary>
/// Writes the report as JSON for machine consumption.
/// </summary>
/// <remarks>
/// Written for a CI gate or a dashboard: the summary block carries the counts a caller needs to
/// decide pass or fail without parsing the findings array at all.
/// </remarks>
public sealed class JsonValidationReportWriter : IValidationReportWriter
{
    private static readonly JsonWriterOptions WriterOptions = new() { Indented = true };

    private readonly IOptionsMonitor<QaQcOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="JsonValidationReportWriter"/> class.</summary>
    /// <param name="options">Live QA/QC settings.</param>
    public JsonValidationReportWriter(IOptionsMonitor<QaQcOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string FormatKey => "json";

    /// <inheritdoc />
    public string FileExtension => ".json";

    /// <inheritdoc />
    public async Task<Result<string>> WriteAsync(
        ValidationReport report,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        QaQcOptions options = _options.CurrentValue;
        string path = Path.HasExtension(outputPath) ? outputPath : outputPath + FileExtension;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            await using FileStream stream = File.Create(path);
            await using Utf8JsonWriter writer = new(stream, WriterOptions);

            writer.WriteStartObject();
            writer.WriteString("runId", report.RunId.ToString());
            writer.WriteString("generatedUtc", report.GeneratedAtUtc.ToString("O"));

            writer.WriteStartObject("summary");
            writer.WriteNumber("total", report.TotalCount);
            writer.WriteString("highestSeverity", report.HighestSeverity.ToString());
            writer.WriteBoolean("acceptable", report.IsAcceptable(options.FailAtOrAbove));

            foreach (IssueSeverity severity in Enum.GetValues<IssueSeverity>())
            {
                writer.WriteNumber(char.ToLowerInvariant(severity.ToString()[0]) + severity.ToString()[1..],
                    report.CountOf(severity));
            }

            writer.WriteEndObject();

            writer.WriteStartArray("findings");

            foreach (ValidationIssue issue in report.AtOrAbove(options.Reporting.MinimumSeverity))
            {
                cancellationToken.ThrowIfCancellationRequested();
                WriteFinding(writer, issue);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            await writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            return Result.Success(path);
        }
        catch (IOException ex)
        {
            return Result.Failure<string>(new Error("Report.IoFailure", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure<string>(new Error("Report.AccessDenied", ex.Message));
        }
    }

    private static void WriteFinding(Utf8JsonWriter writer, ValidationIssue issue)
    {
        writer.WriteStartObject();
        writer.WriteString("severity", issue.Severity.ToString());
        writer.WriteString("category", issue.Category.ToString());
        writer.WriteString("code", issue.Code);
        writer.WriteString("message", issue.Message);

        if (issue.Layer is not null)
        {
            writer.WriteString("layer", issue.Layer.Value);
        }

        if (issue.FeatureId is not null)
        {
            writer.WriteString("featureId", issue.FeatureId);
        }

        if (issue.FieldName is not null)
        {
            writer.WriteString("field", issue.FieldName);
        }

        if (issue.HasLocation)
        {
            writer.WriteStartObject("location");
            writer.WriteNumber("x", issue.LocationX!.Value);
            writer.WriteNumber("y", issue.LocationY!.Value);
            writer.WriteEndObject();
        }

        if (issue.Remediation is not null)
        {
            writer.WriteString("remediation", issue.Remediation);
        }

        writer.WriteEndObject();
    }
}
