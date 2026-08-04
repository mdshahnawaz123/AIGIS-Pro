using AiGisConverter.Ai.Models;

namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// Caches provider responses so that re-running a conversion, or converting drawings that share
/// a layer standard, does not repeat identical inference work.
/// </summary>
public interface IAIResponseCache
{
    /// <summary>Attempts to read a cached response.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="response">The cached response, when present and unexpired.</param>
    /// <returns><see langword="true"/> on a cache hit.</returns>
    bool TryGet(string key, out AIClassificationResponse? response);

    /// <summary>Stores a response.</summary>
    /// <param name="key">The cache key.</param>
    /// <param name="response">The response to store.</param>
    /// <param name="timeToLive">How long the entry stays valid.</param>
    void Set(string key, AIClassificationResponse response, TimeSpan timeToLive);

    /// <summary>Removes all cached entries.</summary>
    void Clear();
}
