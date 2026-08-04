using System.Globalization;
using System.Text;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.QaQc.Options;
using Microsoft.Extensions.Options;

namespace AiGisConverter.QaQc.Reporting;

/// <summary>
/// Writes the findings as CSV, one row per finding.
/// </summary>
/// <remarks>
/// The format an engineer actually works in: sort by severity, filter to their own layer, tick off
/// what they have fixed. A UTF-8 byte-order mark is written so Excel does not mangle non-ASCII
/// layer names on open.
/// </remarks>
public sealed class CsvValidationReportWriter : IValidationReportWriter
{
    private readonly IOptionsMonitor<QaQcOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="CsvValidationReportWriter"/> class.</summary>
    /// <param name="options">Live QA/QC settings.</param>
    public CsvValidationReportWriter(IOptionsMonitor<QaQcOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
    }

    /// <inheritdoc />
    public string FormatKey => "csv";

    /// <inheritdoc />
    public string FileExtension => ".csv";

    /// <inheritdoc />
    public async Task<Result<string>> WriteAsync(
        ValidationReport report,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        string path = Path.HasExtension(outputPath) ? outputPath : outputPath + FileExtension;

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");

            await using StreamWriter writer = new(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

            await writer.WriteLineAsync(
                "severity,category,code,message,layer,featureId,field,x,y,remediation,detectedUtc")
                .ConfigureAwait(false);

            foreach (ValidationIssue issue in report.AtOrAbove(_options.CurrentValue.Reporting.MinimumSeverity))
            {
                cancellationToken.ThrowIfCancellationRequested();

                await writer.WriteLineAsync(string.Join(',',
                [
                    Escape(issue.Severity.ToString()),
                    Escape(issue.Category.ToString()),
                    Escape(issue.Code),
                    Escape(issue.Message),
                    Escape(issue.Layer?.Value ?? string.Empty),
                    Escape(issue.FeatureId ?? string.Empty),
                    Escape(issue.FieldName ?? string.Empty),
                    Escape(Format(issue.LocationX)),
                    Escape(Format(issue.LocationY)),
                    Escape(issue.Remediation ?? string.Empty),
                    Escape(issue.DetectedAtUtc.ToString("O", CultureInfo.InvariantCulture)),
                ])).ConfigureAwait(false);
            }

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

    private static string Format(double? value) =>
        value is null ? string.Empty : value.Value.ToString("G9", CultureInfo.InvariantCulture);

    /// <summary>Applies RFC 4180 quoting.</summary>
    private static string Escape(string value)
    {
        if (value.Length == 0)
        {
            return value;
        }

        bool needsQuoting = value.Contains(',', StringComparison.Ordinal)
                            || value.Contains('"', StringComparison.Ordinal)
                            || value.Contains('\n', StringComparison.Ordinal)
                            || value.Contains('\r', StringComparison.Ordinal);

        if (!needsQuoting)
        {
            return value;
        }

        StringBuilder builder = new(value.Length + 8);
        builder.Append('"').Append(value.Replace("\"", "\"\"", StringComparison.Ordinal)).Append('"');

        return builder.ToString();
    }
}
