using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.QaQc.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.QaQc.Reporting;

/// <summary>
/// Renders a report in every configured format.
/// </summary>
/// <remarks>
/// A format that fails to write does not stop the others. A failed HTML render must not cost the
/// operator the CSV they were going to work from.
/// </remarks>
public sealed class ValidationReportRenderer
{
    private readonly IReadOnlyDictionary<string, IValidationReportWriter> _writers;
    private readonly IOptionsMonitor<QaQcOptions> _options;
    private readonly ILogger<ValidationReportRenderer> _logger;

    /// <summary>Initializes a new instance of the <see cref="ValidationReportRenderer"/> class.</summary>
    /// <param name="writers">Every registered writer.</param>
    /// <param name="options">Live QA/QC settings.</param>
    /// <param name="logger">Logger for rendering diagnostics.</param>
    public ValidationReportRenderer(
        IEnumerable<IValidationReportWriter> writers,
        IOptionsMonitor<QaQcOptions> options,
        ILogger<ValidationReportRenderer> logger)
    {
        ArgumentNullException.ThrowIfNull(writers);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        Dictionary<string, IValidationReportWriter> index = new(StringComparer.OrdinalIgnoreCase);

        foreach (IValidationReportWriter writer in writers)
        {
            index.TryAdd(writer.FormatKey, writer);
        }

        _writers = index;
        _options = options;
        _logger = logger;
    }

    /// <summary>Gets the format keys that can be rendered.</summary>
    public IReadOnlyCollection<string> AvailableFormats => (IReadOnlyCollection<string>)_writers.Keys;

    /// <summary>Renders the report in every configured format.</summary>
    /// <param name="report">The report to render.</param>
    /// <param name="outputPathWithoutExtension">Destination path, without an extension.</param>
    /// <param name="cancellationToken">Token used to cancel rendering.</param>
    /// <returns>The paths written. Formats that failed are logged and omitted.</returns>
    public async Task<IReadOnlyList<string>> RenderAsync(
        ValidationReport report,
        string outputPathWithoutExtension,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPathWithoutExtension);

        List<string> written = [];

        foreach (string format in _options.CurrentValue.Reporting.Formats)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!_writers.TryGetValue(format, out IValidationReportWriter? writer))
            {
                _logger.LogWarning(
                    "No report writer is registered for format '{Format}'. Available: {Available}.",
                    format,
                    string.Join(", ", _writers.Keys));

                continue;
            }

            Domain.Common.Result<string> result =
                await writer.WriteAsync(report, outputPathWithoutExtension, cancellationToken).ConfigureAwait(false);

            if (result.IsSuccess)
            {
                written.Add(result.Value);
            }
            else
            {
                _logger.LogError("The {Format} report could not be written: {Reason}", format, result.Error.Message);
            }
        }

        return written;
    }
}
