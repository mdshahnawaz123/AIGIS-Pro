using AiGisConverter.Application.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Pipelines.Steps;

/// <summary>Reads the drawing into the domain's format-neutral source model.</summary>
public sealed class ReadSourceStage : IPipelineStage
{
    private readonly IDataSourceReaderCatalog _readers;
    private readonly ILogger<ReadSourceStage> _logger;

    /// <summary>Initializes a new instance of the <see cref="ReadSourceStage"/> class.</summary>
    /// <param name="readers">Every reader available, built in or contributed by a plugin.</param>
    /// <param name="logger">Logger for the stage.</param>
    public ReadSourceStage(IDataSourceReaderCatalog readers, ILogger<ReadSourceStage> logger)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(logger);

        _readers = readers;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Read source";

    /// <inheritdoc />
    public int Order => 100;

    /// <inheritdoc />
    public bool IsOptional => false;

    /// <inheritdoc />
    public async Task<Result> ExecuteAsync(
        PipelineContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        IDataSourceReader? reader = _readers.FindReader(context.Source);

        if (reader is null)
        {
            return Result.Failure(new Error(
                "Pipeline.NoReader",
                $"No reader claims '{context.Source.Location}'. Supported extensions: " +
                $"{string.Join(", ", _readers.GetSupportedExtensions())}."));
        }

        _logger.LogInformation(
            "Reading {Location} with the {Format} reader.",
            context.Source.Location,
            reader.FormatKey);

        Result<SourceDocument> read = await reader
            .ReadAsync(context.Source, progress: null, cancellationToken)
            .ConfigureAwait(false);

        if (read.IsFailure)
        {
            return Result.Failure(read.Error);
        }

        context.Document = read.Value;
        context.Run.RecordSourceRead(read.Value.CountElements());

        return Result.Success();
    }
}
