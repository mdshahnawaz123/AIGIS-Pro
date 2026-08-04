using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Exporters;

/// <summary>
/// Shared plumbing for text-based streaming exporters: path resolution, buffered stream creation,
/// progress cadence, cancellation and partial-output cleanup.
/// </summary>
/// <remarks>
/// Cleanup on cancellation matters more than it looks. A truncated GeoJSON or CSV is not
/// detectably invalid to a casual reader &#8212; it simply contains fewer features than the
/// drawing did. Leaving one behind invites someone to use it.
/// </remarks>
public abstract class StreamingExporterBase : IStreamingExporter
{
    /// <summary>Initializes a new instance of the <see cref="StreamingExporterBase"/> class.</summary>
    /// <param name="options">Live GIS settings.</param>
    /// <param name="logger">Logger for the concrete exporter.</param>
    protected StreamingExporterBase(IOptionsMonitor<GisOptions> options, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        Options = options;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string FormatKey { get; }

    /// <inheritdoc />
    public abstract ExportFormat Format { get; }

    /// <inheritdoc />
    public abstract string FileExtension { get; }

    /// <inheritdoc />
    public virtual bool SupportsMultipleLayers => false;

    /// <summary>Gets the live GIS settings.</summary>
    protected IOptionsMonitor<GisOptions> Options { get; }

    /// <summary>Gets the logger.</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<string>>> WriteAsync(
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(features);

        string path = ResolvePath(request.OutputPath);

        try
        {
            EnsureDirectory(path);

            long written = await WriteCoreAsync(path, request, features, progress, cancellationToken)
                .ConfigureAwait(false);

            Logger.LogInformation(
                "Wrote {FeatureCount} features to {Path} as {Format} in {Crs}.",
                written,
                path,
                FormatKey,
                request.CoordinateSystem.Identifier);

            progress?.Report(new ExportProgress(written, $"Wrote {written:N0} features."));

            return Result.Success(GetWrittenPaths(path));
        }
        catch (OperationCanceledException)
        {
            CleanUp(path);
            throw;
        }
        catch (IOException ex)
        {
            CleanUp(path);
            return Result.Failure<IReadOnlyList<string>>(new Error("Export.IoFailure", ex.Message));
        }
        catch (UnauthorizedAccessException ex)
        {
            return Result.Failure<IReadOnlyList<string>>(new Error("Export.AccessDenied", ex.Message));
        }
    }

    /// <summary>Writes the features. Returns how many were written.</summary>
    /// <param name="path">The resolved output path.</param>
    /// <param name="request">What to write.</param>
    /// <param name="features">The features, consumed once.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <returns>The number of features written.</returns>
    protected abstract Task<long> WriteCoreAsync(
        string path,
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress,
        CancellationToken cancellationToken);

    /// <summary>Lists every file the export produced. Overridden by formats writing sidecars.</summary>
    /// <param name="primaryPath">The main output file.</param>
    /// <returns>The paths written.</returns>
    protected virtual IReadOnlyList<string> GetWrittenPaths(string primaryPath) => [primaryPath];

    /// <summary>Removes partial output after a failure.</summary>
    /// <param name="primaryPath">The main output file.</param>
    protected virtual void CleanUp(string primaryPath)
    {
        foreach (string path in GetWrittenPaths(primaryPath))
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Logger.LogWarning(ex, "Could not remove the partial output at {Path}.", path);
            }
        }
    }

    /// <summary>Opens a buffered, asynchronous write stream.</summary>
    /// <param name="path">The file to create.</param>
    /// <returns>The stream.</returns>
    protected FileStream CreateStream(string path) =>
        new(path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            Options.CurrentValue.Streaming.BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    /// <summary>Reports progress on the configured cadence.</summary>
    /// <param name="progress">The sink.</param>
    /// <param name="written">How many features have been written.</param>
    protected void ReportProgress(IProgress<ExportProgress>? progress, long written)
    {
        if (progress is not null && written % Options.CurrentValue.Streaming.ProgressInterval == 0)
        {
            progress.Report(new ExportProgress(written, $"Written {written:N0} features..."));
        }
    }

    private string ResolvePath(string outputPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        return Path.HasExtension(outputPath) ? outputPath : outputPath + FileExtension;
    }

    private static void EnsureDirectory(string path)
    {
        string? directory = Path.GetDirectoryName(path);

        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
    }
}
