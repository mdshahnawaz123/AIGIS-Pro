using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Common;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Pipelines.Steps;

/// <summary>Writes the converted datasets.</summary>
public sealed class ExportStage : IPipelineStage
{
    private readonly IDatasetExportService _exporter;
    private readonly ILogger<ExportStage> _logger;

    /// <summary>Initializes a new instance of the <see cref="ExportStage"/> class.</summary>
    /// <param name="exporter">The export port.</param>
    /// <param name="logger">Logger for the stage.</param>
    public ExportStage(IDatasetExportService exporter, ILogger<ExportStage> logger)
    {
        ArgumentNullException.ThrowIfNull(exporter);
        ArgumentNullException.ThrowIfNull(logger);

        _exporter = exporter;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Export";

    /// <inheritdoc />
    public int Order => 600;

    /// <inheritdoc />
    public bool IsOptional => false;

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        if (context.Datasets.Count == 0)
        {
            // Nothing to write is not a failure. An empty drawing converts to an empty delivery,
            // and the QA report already says the datasets were empty.
            _logger.LogWarning("There is nothing to export: the conversion produced no datasets.");

            return Result.Success();
        }

        string? formatKey = context.Settings.ExportFormats.FirstOrDefault().ToString().ToLowerInvariant();

        Result<IReadOnlyList<string>> exported = await _exporter.ExportAsync(
            context.Datasets,
            new DatasetExportRequest(context.OutputDirectory, context.ProfileId, formatKey),
            progress: null,
            cancellationToken).ConfigureAwait(false);

        if (exported.IsFailure)
        {
            return Result.Failure(exported.Error);
        }

        context.RecordOutputs(exported.Value);

        foreach (string path in exported.Value)
        {
            context.Run.RecordOutput(path);
        }

        _logger.LogInformation("Wrote {FileCount} files to {Directory}.",
            exported.Value.Count, context.OutputDirectory);

        return Result.Success();
    }
}
