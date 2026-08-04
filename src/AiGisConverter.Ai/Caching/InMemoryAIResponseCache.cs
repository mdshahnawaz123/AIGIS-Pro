using System.Collections.Concurrent;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Caching;

/// <summary>
/// Process-local <see cref="IAIResponseCache"/> backed by a concurrent dictionary.
/// </summary>
/// <remarks>
/// <para>
/// Sufficient for a desktop application converting a batch of drawings in one session. It is
/// registered against the interface, so a durable SQLite-backed cache can replace it later
/// without touching a single provider.
/// </para>
/// <para>
/// Entries are cloned on the way in and on the way out. A cache that hands back the instance it
/// stores is not a cache of values but a pool of shared mutable objects, and
/// <see cref="ClassificationResult"/> carries an acceptance flag that each caller stamps according
/// to its own confidence threshold. Cloning on both sides is what makes a hit indistinguishable
/// from a miss to the caller.
/// </para>
/// </remarks>
public sealed class InMemoryAIResponseCache : IAIResponseCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="InMemoryAIResponseCache"/> class.</summary>
    /// <param name="timeProvider">Clock abstraction, injected so expiry is testable.</param>
    public InMemoryAIResponseCache(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public bool TryGet(string key, out AIClassificationResponse? response)
    {
        response = null;

        if (!_entries.TryGetValue(key, out CacheEntry? entry))
        {
            return false;
        }

        if (entry.ExpiresAt <= _timeProvider.GetUtcNow())
        {
            _entries.TryRemove(key, out _);
            return false;
        }

        // A copy, so that whatever the caller does to the acceptance flags cannot reach the
        // instance the next caller will be given.
        response = ClassificationResponseCloner.Clone(entry.Response);
        return true;
    }

    /// <inheritdoc />
    public void Set(string key, AIClassificationResponse response, TimeSpan timeToLive)
    {
        ArgumentNullException.ThrowIfNull(response);

        // A copy on the way in as well: the caller keeps its own instance and may still be
        // stamping acceptance onto it after this call returns.
        _entries[key] = new CacheEntry(
            ClassificationResponseCloner.Clone(response),
            _timeProvider.GetUtcNow().Add(timeToLive));
    }

    /// <inheritdoc />
    public void Clear() => _entries.Clear();

    private sealed record CacheEntry(AIClassificationResponse Response, DateTimeOffset ExpiresAt);
}
