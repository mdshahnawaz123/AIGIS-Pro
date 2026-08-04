using System.Globalization;
using System.Net;
using System.Text;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.QaQc.Options;
using Microsoft.Extensions.Options;

namespace AiGisConverter.QaQc.Reporting;

/// <summary>
/// Writes a self-contained HTML report.
/// </summary>
/// <remarks>
/// <para>
/// Styles are inline and there are no external references of any kind. A QA report is emailed,
/// zipped and opened from a network share by people who will never have the application installed;
/// anything it has to fetch is a thing it will fail to fetch.
/// </para>
/// <para>
/// Every value is HTML-encoded. Layer names come from CAD files written by anyone, and a layer
/// called <c>&lt;script&gt;</c> should render as text.
/// </para>
/// </remarks>
public sealed class HtmlValidationReportWriter : IValidationReportWriter
{
    private readonly IOptionsMonitor<QaQcOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="HtmlValidationReportWriter"/> class.</summary>
    /// <param name="options">Live QA/QC settings.</param>
    public HtmlValidationReportWriter(IOptionsMonitor<QaQcOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string FormatKey => "html";

    /// <inheritdoc />
    public string FileExtension => ".html";

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

            StringBuilder html = new(16_384);
            WriteHead(html, report, options);
            WriteSummary(html, report, options);
            WriteFindings(html, report, options, cancellationToken);
            html.Append("</body></html>");

            await File.WriteAllTextAsync(path, html.ToString(), new UTF8Encoding(false), cancellationToken)
                .ConfigureAwait(false);

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

    private static void WriteHead(StringBuilder html, ValidationReport report, QaQcOptions options)
    {
        bool acceptable = report.IsAcceptable(options.FailAtOrAbove);

        html.Append("<!DOCTYPE html><html lang=\"en\"><head><meta charset=\"utf-8\">")
            .Append("<title>Conversion QA report</title><style>")
            .Append("body{font:14px/1.5 system-ui,sans-serif;margin:2rem;color:#1a1a1a}")
            .Append("h1{font-size:1.4rem;margin:0 0 .25rem}")
            .Append(".meta{color:#666;font-size:.85rem;margin-bottom:1.5rem}")
            .Append(".verdict{display:inline-block;padding:.4rem .9rem;border-radius:4px;font-weight:600}")
            .Append(".pass{background:#e6f4ea;color:#0b6b2f}.fail{background:#fce8e6;color:#8c1d18}")
            .Append("table{border-collapse:collapse;width:100%;margin-top:1rem}")
            .Append("th,td{text-align:left;padding:.45rem .6rem;border-bottom:1px solid #e5e5e5;vertical-align:top}")
            .Append("th{background:#fafafa;font-weight:600;position:sticky;top:0}")
            .Append("td.sev{font-weight:600;white-space:nowrap}")
            .Append(".Critical{color:#8c1d18}.Error{color:#b3261e}.Warning{color:#8a6100}.Information{color:#555}")
            .Append("code{background:#f4f4f4;padding:.1rem .3rem;border-radius:3px;font-size:.85em}")
            .Append(".rem{color:#555;font-style:italic}")
            .Append("</style></head><body>")
            .Append("<h1>Conversion QA report</h1><div class=\"meta\">Run ")
            .Append(Encode(report.RunId.ToString()))
            .Append(" &middot; generated ")
            .Append(Encode(report.GeneratedAtUtc.ToString("u", CultureInfo.InvariantCulture)))
            .Append("</div><p><span class=\"verdict ")
            .Append(acceptable ? "pass" : "fail")
            .Append("\">")
            .Append(acceptable ? "Acceptable" : "Rejected")
            .Append("</span> against a failure threshold of ")
            .Append(Encode(options.FailAtOrAbove.ToString()))
            .Append(".</p>");
    }

    private static void WriteSummary(StringBuilder html, ValidationReport report, QaQcOptions options)
    {
        html.Append("<table><tr><th>Severity</th><th>Count</th></tr>");

        foreach (IssueSeverity severity in Enum.GetValues<IssueSeverity>().OrderByDescending(static s => s))
        {
            html.Append("<tr><td class=\"sev ").Append(severity).Append("\">")
                .Append(Encode(severity.ToString()))
                .Append("</td><td>").Append(report.CountOf(severity)).Append("</td></tr>");
        }

        html.Append("<tr><th>Total</th><th>").Append(report.TotalCount).Append("</th></tr></table>");
    }

    private static void WriteFindings(
        StringBuilder html,
        ValidationReport report,
        QaQcOptions options,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ValidationIssue> findings = report.AtOrAbove(options.Reporting.MinimumSeverity);

        if (findings.Count == 0)
        {
            html.Append("<p>No findings at or above ")
                .Append(Encode(options.Reporting.MinimumSeverity.ToString()))
                .Append(".</p>");

            return;
        }

        foreach (IGrouping<string, ValidationIssue> group in Group(findings, options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            html.Append("<h2>").Append(Encode(group.Key)).Append(" <small>(")
                .Append(group.Count()).Append(")</small></h2>")
                .Append("<table><tr><th>Severity</th><th>Feature</th><th>Field</th>")
                .Append("<th>Location</th><th>Message</th></tr>");

            foreach (ValidationIssue issue in group)
            {
                html.Append("<tr><td class=\"sev ").Append(issue.Severity).Append("\">")
                    .Append(Encode(issue.Severity.ToString())).Append("</td><td>")
                    .Append(issue.FeatureId is null ? "&mdash;" : Encode(issue.FeatureId)).Append("</td><td>")
                    .Append(issue.FieldName is null ? string.Empty : Encode(issue.FieldName)).Append("</td><td>")
                    .Append(issue.HasLocation
                        ? Encode(string.Create(
                            CultureInfo.InvariantCulture,
                            $"{issue.LocationX!.Value:G9}, {issue.LocationY!.Value:G9}"))
                        : string.Empty)
                    .Append("</td><td>").Append(Encode(issue.Message));

                if (issue.Remediation is not null)
                {
                    html.Append("<div class=\"rem\">").Append(Encode(issue.Remediation)).Append("</div>");
                }

                html.Append("</td></tr>");
            }

            html.Append("</table>");
        }
    }

    private static IEnumerable<IGrouping<string, ValidationIssue>> Group(
        IReadOnlyList<ValidationIssue> findings,
        QaQcOptions options) =>
        options.Reporting.GroupByRule
            ? findings.GroupBy(static i => i.Code).OrderBy(static g => g.Key, StringComparer.Ordinal)
            : findings.GroupBy(static i => i.Category.ToString()).OrderBy(static g => g.Key, StringComparer.Ordinal);

    /// <summary>Encodes a value for HTML. Layer names come from files written by anyone.</summary>
    private static string Encode(string value) => WebUtility.HtmlEncode(value);
}
