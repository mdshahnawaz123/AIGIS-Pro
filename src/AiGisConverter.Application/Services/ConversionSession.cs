using AiGisConverter.Application.Abstractions;

namespace AiGisConverter.Application.Services;

/// <summary>
/// Default <see cref="IConversionSession"/>: an in-memory, thread-safe holder for the current drawing.
/// </summary>
/// <remarks>
/// Registered as a singleton so every screen shares one instance. Publishing happens on a pipeline
/// worker thread while consumers read on the UI thread, so the snapshot reference is guarded and
/// swapped atomically; consumers marshal onto their own thread inside the <see cref="Changed"/>
/// handler.
/// </remarks>
public sealed class ConversionSession : IConversionSession
{
    private const int MaxHistory = 50;

    private readonly object _gate = new();
    private readonly List<ConversionSessionSnapshot> _history = [];
    private ConversionSessionSnapshot? _current;

    /// <inheritdoc />
    public ConversionSessionSnapshot? Current
    {
        get
        {
            lock (_gate)
            {
                return _current;
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ConversionSessionSnapshot> History
    {
        get
        {
            lock (_gate)
            {
                return [.. _history];
            }
        }
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public void Publish(ConversionSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            _current = snapshot;

            // Newest first, and bounded: a long batch run must not grow the history without limit.
            _history.Insert(0, snapshot);

            if (_history.Count > MaxHistory)
            {
                _history.RemoveRange(MaxHistory, _history.Count - MaxHistory);
            }
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Reopen(ConversionSessionSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        lock (_gate)
        {
            // Reopening only changes what is current; the history itself is left untouched.
            _current = snapshot;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public void Clear()
    {
        lock (_gate)
        {
            if (_current is null)
            {
                return;
            }

            _current = null;
        }

        Changed?.Invoke(this, EventArgs.Empty);
    }
}
