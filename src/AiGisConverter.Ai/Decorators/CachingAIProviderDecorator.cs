using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Caching;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Decorators;

/// <summary>
/// Serves repeat requests from <see cref="IAIResponseCache"/>. Innermost of the standard
/// decorators, so a cache hit avoids both the retry policy and the engine call.
/// </summary>
public sealed class CachingAIProviderDecorator : IAIProviderDecorator
{
    private readonly IAIResponseCache _cache;
    private readonly AIRequestCacheKeyFactory _keyFactory;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes a new instance of the <see cref="CachingAIProviderDecorator"/> class.</summary>
    /// <param name="cache">The response cache.</param>
    /// <param name="keyFactory">Builds stable cache keys.</param>
    /// <param name="options">Live AI layer options.</param>
    /// <param name="loggerFactory">Factory used to create a logger per wrapped provider.</param>
    public CachingAIProviderDecorator(
        IAIResponseCache cache,
        AIRequestCacheKeyFactory keyFactory,
        IOptionsMonitor<AiOptions> options,
        ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(keyFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _cache = cache;
        _keyFactory = keyFactory;
        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public int Order => 20;

    /// <inheritdoc />
    public IAIProvider Decorate(IAIProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        return new CachingProvider(
            inner,
            _cache,
            _keyFactory,
            _options,
            _loggerFactory.CreateLogger($"AiGisConverter.Ai.Cache.{inner.Key}"));
    }

    private sealed class CachingProvider : IAIProvider
    {
        private readonly IAIProvider _inner;
        private readonly IAIResponseCache _cache;
        private readonly AIRequestCacheKeyFactory _keyFactory;
        private readonly IOptionsMonitor<AiOptions> _options;
        private readonly ILogger _logger;

        public CachingProvider(
            IAIProvider inner,
            IAIResponseCache cache,
            AIRequestCacheKeyFactory keyFactory,
            IOptionsMonitor<AiOptions> options,
            ILogger logger)
        {
            _inner = inner;
            _cache = cache;
            _keyFactory = keyFactory;
            _options = options;
            _logger = logger;
        }

        public string Key => _inner.Key;

        public AIProviderMetadata Metadata => _inner.Metadata;

        public Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default) =>
            _inner.ProbeAsync(cancellationToken);

        public async Task<AIClassificationResponse> ClassifyAsync(
            AIClassificationRequest request,
            CancellationToken cancellationToken = default)
        {
            AiOptions options = _options.CurrentValue;

            if (!options.EnableCaching)
            {
                return await _inner.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);
            }

            string cacheKey = _keyFactory.Create(Key, request);

            if (_cache.TryGet(cacheKey, out AIClassificationResponse? cached) && cached is not null)
            {
                _logger.LogDebug("Cache hit for provider {ProviderKey}.", Key);
                return cached;
            }

            AIClassificationResponse response =
                await _inner.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);

            _cache.Set(cacheKey, response, TimeSpan.FromMinutes(options.CacheTimeToLiveMinutes));
            return response;
        }
    }
}
