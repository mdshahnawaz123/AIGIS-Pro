using System.Collections.Concurrent;
using System.Diagnostics;
using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Services.Conversion;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Services.Batch;

/// <summary>Converts many drawings.</summary>
public interface IBatchConversionService
{
    /// <summary>Converts every job in a project.</summary>
    /// <param name="project">The project whose jobs are run.</param>
    /// <param name="outputDirectory">Where the outputs are written.</param>
    /// <param name="options">Batch behaviour.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the batch.</param>
    /// <returns>The outcome of every job.</returns>
    Task<BatchResult> ConvertAsync(
        ConversionProject project,
        string outputDirectory,
        BatchOptions? options = null,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>How a batch behaves.</summary>
/// <param name="MaxConcurrency">How many drawings convert at once. One is serial.</param>
/// <param name="ContinueOnError">Whether a failed drawing stops the batch.</param>
/// <param name="ProfileId">Conversion profile to apply, or null for the default.</param>
/// <param name="SubfolderPerJob">Whether each drawing gets its own output folder.</param>
public sealed record BatchOptions(
    int MaxConcurrency = 2,
    bool ContinueOnError = true,
    string? ProfileId = null,
    bool SubfolderPerJob = true);

/// <summary>Progress through a batch.</summary>
/// <param name="Completed">Jobs finished, successfully or not.</param>
/// <param name="Total">Jobs in the batch.</param>
/// <param name="CurrentFile">The file most recently started.</param>
public readonly record struct BatchProgress(int Completed, int Total, string CurrentFile);

/// <summary>What a batch produced.</summary>
/// <param name="Succeeded">Runs that finished.</param>
/// <param name="Failed">Jobs that did not, with the reason.</param>
/// <param name="Duration">Wall-clock duration of the whole batch.</param>
public sealed record BatchResult(
    IReadOnlyList<ConversionRun> Succeeded,
    IReadOnlyList<(ConversionJobId JobId, string Location, Error Error)> Failed,
    TimeSpan Duration)
{
    /// <summary>Gets the number of jobs attempted.</summary>
    public int Total => Succeeded.Count + Failed.Count;

    /// <summary>Gets a value indicating whether every job finished.</summary>
    public bool IsCompleteSuccess => Failed.Count == 0;
}

/// <summary>
/// Default <see cref="IBatchConversionService"/>.
/// </summary>
/// <remarks>
/// <para>
/// Concurrency is bounded and low by default. Conversion is memory-bound rather than CPU-bound
/// &#8212; a large drawing holds its whole source model &#8212; so running one per core is the
/// fastest way to exhaust memory on a workstation rather than the fastest way to finish.
/// </para>
/// <para>
/// Each job gets its own service scope. The unit of work and its change tracker are scoped, and
/// sharing one across parallel conversions would have two runs writing to the same tracker.
/// </para>
/// </remarks>
public sealed class BatchConversionService : IBatchConversionService
{
    private readonly IConversionScopeFactory _scopes;
    private readonly ILogger<BatchConversionService> _logger;

    /// <summary>Initializes a new instance of the <see cref="BatchConversionService"/> class.</summary>
    /// <param name="scopes">Creates an isolated conversion service per job.</param>
    /// <param name="logger">Logger for the batch.</param>
    public BatchConversionService(IConversionScopeFactory scopes, ILogger<BatchConversionService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(logger);

        _scopes = scopes;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<BatchResult> ConvertAsync(
        ConversionProject project,
        string outputDirectory,
        BatchOptions? options = null,
        IProgress<BatchProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        BatchOptions settings = options ?? new BatchOptions();
        IReadOnlyList<ConversionJob> jobs = project.Jobs;

        ConcurrentBag<ConversionRun> succeeded = [];
        ConcurrentBag<(ConversionJobId, string, Error)> failed = [];

        long startedAt = Stopwatch.GetTimestamp();
        int completed = 0;

        using CancellationTokenSource batchCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        try
        {
            await Parallel.ForEachAsync(
                jobs,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, settings.MaxConcurrency),
                    CancellationToken = batchCts.Token,
                },
                async (job, token) =>
                {
                    progress?.Report(new BatchProgress(
                        Volatile.Read(ref completed), jobs.Count, job.Source.Location));

                    string destination = settings.SubfolderPerJob
                        ? Path.Combine(outputDirectory, SafeFolderName(job.Source.Location))
                        : outputDirectory;

                    Result<ConversionRun> result = await RunOneAsync(
                        job, project.Settings, destination, settings, token).ConfigureAwait(false);

                    if (result.IsSuccess)
                    {
                        succeeded.Add(result.Value);
                    }
                    else
                    {
                        failed.Add((job.Id, job.Source.Location, result.Error));

                        if (!settings.ContinueOnError)
                        {
                            // Stop the remaining jobs; those already running finish and report.
                            await batchCts.CancelAsync().ConfigureAwait(false);
                        }
                    }

                    progress?.Report(new BatchProgress(
                        Interlocked.Increment(ref completed), jobs.Count, job.Source.Location));
                }).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Stopped by the fail-fast policy rather than by the caller. The failures explain why.
            _logger.LogWarning("The batch stopped early because a job failed and ContinueOnError is off.");
        }

        BatchResult batch = new(
            [.. succeeded],
            [.. failed],
            Stopwatch.GetElapsedTime(startedAt));

        _logger.LogInformation(
            "Batch finished in {ElapsedMs} ms: {Succeeded} succeeded, {Failed} failed.",
            batch.Duration.TotalMilliseconds,
            batch.Succeeded.Count,
            batch.Failed.Count);

        return batch;
    }

    private async Task<Result<ConversionRun>> RunOneAsync(
        ConversionJob job,
        Domain.ValueObjects.ConversionSettings settings,
        string destination,
        BatchOptions options,
        CancellationToken cancellationToken)
    {
        await using IConversionScope scope = _scopes.Create();

        try
        {
            return await scope.Service.ConvertAsync(
                job,
                new ConversionSettingsSnapshot(settings),
                destination,
                options.ProfileId,
                progress: null,
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Job {Location} failed unexpectedly.", job.Source.Location);

            return Result.Failure<ConversionRun>(new Error("Batch.JobThrew", ex.Message));
        }
    }

    /// <summary>Turns a file path into a folder name that will not collide or be rejected.</summary>
    private static string SafeFolderName(string location)
    {
        string stem = Path.GetFileNameWithoutExtension(location);
        char[] invalid = Path.GetInvalidFileNameChars();

        Span<char> buffer = stackalloc char[stem.Length];

        for (int i = 0; i < stem.Length; i++)
        {
            buffer[i] = Array.IndexOf(invalid, stem[i]) >= 0 ? '_' : stem[i];
        }

        return new string(buffer);
    }
}

/// <summary>Creates an isolated conversion service, with its own unit of work.</summary>
/// <remarks>
/// A factory rather than an injected service because the change tracker is scoped and two parallel
/// conversions sharing one would write to the same tracker. The composition root supplies the
/// implementation, since only it knows the container.
/// </remarks>
public interface IConversionScopeFactory
{
    /// <summary>Creates a scope.</summary>
    /// <returns>The scope. Dispose it when the job is finished.</returns>
    IConversionScope Create();
}

/// <summary>One conversion's isolated services.</summary>
public interface IConversionScope : IAsyncDisposable
{
    /// <summary>Gets the conversion service for this scope.</summary>
    IConversionService Service { get; }
}
