using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Factories;

/// <summary>
/// Default <see cref="IAIProviderFactory"/>. Indexes every provider offered by every
/// <see cref="IAIProviderSource"/> by its own <see cref="IAIProvider.Key"/>, and selects one by
/// configuration string.
/// </summary>
/// <remarks>
/// <para>
/// There is no switch statement, no enum of vendors and no compile-time knowledge of any provider
/// here. Registering a new provider &#8212; in code or by dropping in a plugin &#8212; makes it
/// selectable; nothing in this class changes.
/// </para>
/// <para>
/// The index is built on first use rather than in the constructor, because plugin-contributed
/// providers do not exist until after the container has been built.
/// </para>
/// </remarks>
public sealed class AIProviderFactory : IAIProviderFactory
{
    private readonly IEnumerable<IAIProviderSource> _sources;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly ILogger<AIProviderFactory> _logger;
    private readonly object _gate = new();

    private IReadOnlyDictionary<string, IAIProvider>? _index;

    /// <summary>Initializes a new instance of the <see cref="AIProviderFactory"/> class.</summary>
    /// <param name="sources">Everything that can offer providers.</param>
    /// <param name="options">Live AI layer options.</param>
    /// <param name="logger">Logger for resolution diagnostics.</param>
    public AIProviderFactory(
        IEnumerable<IAIProviderSource> sources,
        IOptionsMonitor<AiOptions> options,
        ILogger<AIProviderFactory> logger)
    {
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _sources = sources;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public IAIProvider GetActiveProvider()
    {
        string key = _options.CurrentValue.ActiveProvider;

        return string.IsNullOrWhiteSpace(key) ? GetDefaultProvider() : GetProvider(key);
    }

    /// <inheritdoc />
    public IAIProvider? GetFallbackProvider()
    {
        string key = _options.CurrentValue.FallbackProvider;

        if (string.Equals(key, AiOptions.DisabledFallback, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            return GetIndex().Count == 0 ? null : GetDefaultProvider();
        }

        return TryGetProvider(key, out IAIProvider? provider) ? provider : null;
    }

    /// <inheritdoc />
    public IAIProvider GetProvider(string key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);

        IReadOnlyDictionary<string, IAIProvider> index = GetIndex();

        return index.TryGetValue(key, out IAIProvider? provider)
            ? provider
            : throw AIProviderNotRegisteredException.For(key, index.Keys);
    }

    /// <inheritdoc />
    public bool TryGetProvider(string key, out IAIProvider? provider)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            provider = null;
            return false;
        }

        return GetIndex().TryGetValue(key, out provider);
    }

    /// <inheritdoc />
    public IReadOnlyList<AIProviderMetadata> GetRegisteredProviders() =>
        GetIndex().Values
            .Select(static p => p.Metadata)
            .OrderBy(static m => m.Key, StringComparer.Ordinal)
            .ToList();

    /// <inheritdoc />
    public void Refresh()
    {
        lock (_gate)
        {
            _index = null;
        }

        _logger.LogDebug("The AI provider index was invalidated and will be rebuilt on next use.");
    }

    /// <summary>Builds, or returns, the provider index.</summary>
    private IReadOnlyDictionary<string, IAIProvider> GetIndex()
    {
        IReadOnlyDictionary<string, IAIProvider>? current = _index;

        if (current is not null)
        {
            return current;
        }

        lock (_gate)
        {
            if (_index is not null)
            {
                return _index;
            }

            Dictionary<string, IAIProvider> index = new(StringComparer.OrdinalIgnoreCase);

            foreach (IAIProviderSource source in _sources)
            {
                foreach (IAIProvider provider in source.GetProviders())
                {
                    if (index.TryAdd(provider.Key, provider))
                    {
                        continue;
                    }

                    // A plugin that collides with a built-in key is ignored rather than fatal:
                    // one badly-named third-party plugin must not disable AI classification.
                    _logger.LogWarning(
                        "Two AI providers declare the key '{ProviderKey}'. The first registration " +
                        "was kept and the duplicate from {SourceType} was ignored.",
                        provider.Key,
                        source.GetType().Name);
                }
            }

            _index = index;
            _logger.LogInformation(
                "Indexed {Count} AI providers: {Keys}.",
                index.Count,
                string.Join(", ", index.Keys.OrderBy(static k => k, StringComparer.Ordinal)));

            return index;
        }
    }

    /// <summary>
    /// Selects a provider when configuration names none: the least demanding one available,
    /// preferring providers that need no network and no downloaded model.
    /// </summary>
    private IAIProvider GetDefaultProvider()
    {
        IAIProvider? selected = GetIndex().Values
            .OrderBy(static p => p.Metadata.RequiresNetwork ? 1 : 0)
            .ThenBy(static p => (int)p.Metadata.Kind)
            .ThenBy(static p => p.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        return selected ?? throw new AIProviderNotRegisteredException(
            "No AI provider is available. Register one with AddAiLayer(...) or install an AI provider plugin.");
    }
}
