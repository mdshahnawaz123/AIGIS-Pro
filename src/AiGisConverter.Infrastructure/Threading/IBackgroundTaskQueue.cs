using System.Threading.Channels;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Infrastructure.Threading;

/// <summary>
/// A bounded queue of background work.
/// </summary>
/// <remarks>
/// Bounded on purpose. An unbounded queue turns a user holding down a button into an
/// out-of-memory failure, and the back-pressure a bound provides is the only honest way to tell
/// the producer it is going too fast.
/// </remarks>
public interface IBackgroundTaskQueue
{
    /// <summary>Gets the number of items waiting.</summary>
    int Count { get; }

    /// <summary>Queues work, waiting if the queue is full.</summary>
    /// <param name="work">The work to run.</param>
    /// <param name="cancellationToken">Token used to cancel the enqueue.</param>
    /// <returns>A task that completes when the item has been accepted.</returns>
    ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> work,
        CancellationToken cancellationToken = default);

    /// <summary>Takes the next item, waiting until one is available.</summary>
    /// <param name="cancellationToken">Token used to cancel the wait.</param>
    /// <returns>The next item of work.</returns>
    ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(CancellationToken cancellationToken = default);

    /// <summary>Signals that no further work will be queued.</summary>
    void Complete();
}

/// <summary>Channel-backed <see cref="IBackgroundTaskQueue"/>.</summary>
public sealed class BackgroundTaskQueue : IBackgroundTaskQueue
{
    private readonly Channel<Func<CancellationToken, ValueTask>> _channel;
    private readonly ILogger<BackgroundTaskQueue> _logger;

    /// <summary>Initializes a new instance of the <see cref="BackgroundTaskQueue"/> class.</summary>
    /// <param name="logger">Logger for queue diagnostics.</param>
    /// <param name="capacity">How many items may wait before producers are made to wait.</param>
    public BackgroundTaskQueue(ILogger<BackgroundTaskQueue> logger, int capacity = 128)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);

        _logger = logger;
        _channel = Channel.CreateBounded<Func<CancellationToken, ValueTask>>(
            new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false,
            });
    }

    /// <inheritdoc />
    public int Count => _channel.Reader.CanCount ? _channel.Reader.Count : 0;

    /// <inheritdoc />
    public async ValueTask EnqueueAsync(
        Func<CancellationToken, ValueTask> work,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(work);

        await _channel.Writer.WriteAsync(work, cancellationToken).ConfigureAwait(false);

        _logger.LogTrace("Work queued. {Count} items waiting.", Count);
    }

    /// <inheritdoc />
    public ValueTask<Func<CancellationToken, ValueTask>> DequeueAsync(
        CancellationToken cancellationToken = default) =>
        _channel.Reader.ReadAsync(cancellationToken);

    /// <inheritdoc />
    public void Complete() => _channel.Writer.TryComplete();
}
