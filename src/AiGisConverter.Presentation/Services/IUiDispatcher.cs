using System;
using System.Threading.Tasks;
using System.Windows.Threading;

namespace AiGisConverter.Presentation.Services;

/// <summary>
/// Marshals work onto the user-interface thread.
/// </summary>
/// <remarks>
/// A view model must not know it is running under WPF. Progress arrives from a conversion running
/// on the thread pool, and updating an observable collection from there throws; this is the seam
/// that fixes it without importing <see cref="Dispatcher"/> into every view model, and it is what
/// lets those view models be tested with no dispatcher at all.
/// </remarks>
public interface IUiDispatcher
{
    /// <summary>Gets a value indicating whether the caller is already on the interface thread.</summary>
    bool IsOnUiThread { get; }

    /// <summary>Runs an action on the interface thread, without waiting.</summary>
    /// <param name="action">The work to run.</param>
    void Post(Action action);

    /// <summary>Runs an action on the interface thread and waits for it.</summary>
    /// <param name="action">The work to run.</param>
    /// <returns>A task that completes when the work has run.</returns>
    Task InvokeAsync(Action action);
}

/// <summary>The WPF dispatcher.</summary>
public sealed class WpfUiDispatcher : IUiDispatcher
{
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current?.Dispatcher
                                              ?? Dispatcher.CurrentDispatcher;

    /// <inheritdoc />
    public bool IsOnUiThread => _dispatcher.CheckAccess();

    /// <inheritdoc />
    public void Post(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsOnUiThread)
        {
            action();
            return;
        }

        _dispatcher.BeginInvoke(action);
    }

    /// <inheritdoc />
    public Task InvokeAsync(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (IsOnUiThread)
        {
            action();
            return Task.CompletedTask;
        }

        return _dispatcher.InvokeAsync(action).Task;
    }
}
