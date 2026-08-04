using System.Collections.Concurrent;
using AiGisConverter.Application.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Application.Notifications;

/// <summary>
/// In-process <see cref="INotificationService"/> with a bounded history.
/// </summary>
/// <remarks>
/// <para>
/// Bounded because a batch of ten thousand drawings raises ten thousand notifications, and a list
/// nobody trims is a memory leak with a friendly name.
/// </para>
/// <para>
/// Handlers are invoked on the publishing thread, and a handler that throws is contained: a
/// misbehaving toast must not fail the conversion that raised it.
/// </para>
/// </remarks>
public sealed class NotificationService : INotificationService
{
    private const int HistoryLimit = 500;

    private readonly ConcurrentQueue<Notification> _history = new();
    private readonly ILogger<NotificationService> _logger;

    /// <summary>Initializes a new instance of the <see cref="NotificationService"/> class.</summary>
    /// <param name="logger">Logger for notification diagnostics.</param>
    public NotificationService(ILogger<NotificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public event EventHandler<Notification>? Published;

    /// <inheritdoc />
    public void Publish(Notification notification)
    {
        ArgumentNullException.ThrowIfNull(notification);

        _history.Enqueue(notification);

        while (_history.Count > HistoryLimit && _history.TryDequeue(out _))
        {
            // Trim the oldest.
        }

        Log(notification);

        try
        {
            Published?.Invoke(this, notification);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "A notification handler threw and was ignored.");
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Notification> GetRecent(int limit = 50)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(limit);

        return [.. _history.Reverse().Take(limit)];
    }

    private void Log(Notification notification)
    {
        switch (notification.Level)
        {
            case NotificationLevel.Error:
                _logger.LogError("{Title} {Detail}", notification.Title, notification.Detail);
                break;
            case NotificationLevel.Warning:
                _logger.LogWarning("{Title} {Detail}", notification.Title, notification.Detail);
                break;
            default:
                _logger.LogInformation("{Title} {Detail}", notification.Title, notification.Detail);
                break;
        }
    }
}
