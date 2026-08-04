using System.Threading.Channels;
using AiGisConverter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Jobs;

/// <summary>Queues conversions and runs them in the background.</summary>
public interface IJobEngine : IAsyncDisposable
{
    /// <summary>Gets the number of jobs waiting.</summary>
    int QueueDepth { get; }

    /// <summary>Gets a value indicating whether the worker loop is running.</summary>
    bool IsRunning { get; }

    /// <summary>Queues work.</summary>
    /// <param name="descriptor">What to run.</param>
    /// <param name="cancellationToken">Token used to cancel the enqueue.</param>
    /// <returns>A task that completes when the work has been accepted.</returns>
    ValueTask EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default);

    /// <summary>Starts the worker loop.</summary>
    /// <param name="cancellationToken">Token that stops the loop.</param>
    /// <returns>A task that completes when the loop ends.</returns>
    Task RunAsync(CancellationToken cancellationToken = default);

    /// <summary>Signals that no further work will be queued.</summary>
    void Complete();
}

/// <summary>One unit of queued work.</summary>
/// <param name="Name">What to call it in logs and progress.</param>
/// <param name="Work">The work to run.</param>
public sealed record JobDescriptor(string Name, Func<CancellationToken, Task> Work);

/// <summary>
/// Channel-backed <see cref="IJobEngine"/>.
/// </summary>
/// <remarks>
/// <para>
/// Bounded, so a user holding down a button applies back-pressure rather than filling memory.
/// </para>
/// <para>
/// A job that throws is logged and the loop continues. The engine's contract is that queued work
/// is attempted, not that it succeeds; a failing job must not stop the ones behind it.
/// </para>
/// </remarks>
public sealed class JobEngine : IJobEngine
{
    private readonly Channel<JobDescriptor> _channel;
    private readonly ILogger<JobEngine> _logger;

    private int _running;

    /// <summary>Initializes a new instance of the <see cref="JobEngine"/> class.</summary>
    /// <param name="logger">Logger for the engine.</param>
    /// <param name="capacity">How many jobs may wait before producers are made to wait.</param>
    public JobEngine(ILogger<JobEngine> logger, int capacity = 64)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _logger = logger;
        _channel = Channel.CreateBounded<JobDescriptor>(new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
    }

    /// <inheritdoc />
    public int QueueDepth => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <inheritdoc />
    public bool IsRunning => Volatile.Read(ref _running) == 1;

    /// <inheritdoc />
    public ValueTask EnqueueAsync(JobDescriptor descriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        return _channel.Writer.WriteAsync(descriptor, cancellationToken);
    }

    /// <inheritdoc />
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Exchange(ref _running, 1);

        try
        {
            await foreach (JobDescriptor descriptor in
                _channel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await ExecuteAsync(descriptor, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("The job engine stopped. {Depth} jobs were still queued.", QueueDepth);
        }
        finally
        {
            Interlocked.Exchange(ref _running, 0);
        }
    }

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        Complete();

        return ValueTask.CompletedTask;
    }

    private async Task ExecuteAsync(JobDescriptor descriptor, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Job {Name} started. {Depth} still queued.", descriptor.Name, QueueDepth);

            await descriptor.Work(cancellationToken).ConfigureAwait(false);

            _logger.LogInformation("Job {Name} finished.", descriptor.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The contract is that queued work is attempted, not that it succeeds.
            _logger.LogError(ex, "Job {Name} failed. The queue continues.", descriptor.Name);
        }
    }
}
