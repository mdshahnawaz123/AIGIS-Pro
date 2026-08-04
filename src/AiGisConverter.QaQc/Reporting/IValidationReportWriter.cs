using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;

namespace AiGisConverter.QaQc.Reporting;

/// <summary>Renders a validation report to a file.</summary>
public interface IValidationReportWriter
{
    /// <summary>Gets the format key, for example <c>html</c>.</summary>
    string FormatKey { get; }

    /// <summary>Gets the file extension, including the leading dot.</summary>
    string FileExtension { get; }

    /// <summary>Writes the report.</summary>
    /// <param name="report">The report to render.</param>
    /// <param name="outputPath">Destination path, with or without the extension.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <returns>The path written, or a failure describing why it could not be.</returns>
    Task<Result<string>> WriteAsync(
        ValidationReport report,
        string outputPath,
        CancellationToken cancellationToken = default);
}
