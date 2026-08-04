using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// Default <see cref="ICapabilityRegistry"/>. Thread-safe, and able to drop a plugin's
/// contributions when it is unloaded.
/// </summary>
internal sealed class CapabilityRegistry : ICapabilityRegistry
{
    private readonly ConcurrentDictionary<Type, List<Entry>> _byContract = new();
    private readonly ILogger<CapabilityRegistry> _logger;
    private readonly object _gate = new();

    public CapabilityRegistry(ILogger<CapabilityRegistry> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<TContract> GetCapabilities<TContract>()
        where TContract : class
    {
        if (!_byContract.TryGetValue(typeof(TContract), out List<Entry>? entries))
        {
            return [];
        }

        lock (_gate)
        {
            return entries
                .OrderBy(static e => e.LoadOrder)
                .Select(static e => e.Resolve())
                .OfType<TContract>()
                .ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<(string PluginId, TContract Capability)> GetCapabilitiesWithSource<TContract>()
        where TContract : class
    {
        if (!_byContract.TryGetValue(typeof(TContract), out List<Entry>? entries))
        {
            return [];
        }

        lock (_gate)
        {
            return entries
                .OrderBy(static e => e.LoadOrder)
                .Select(static e => (e.PluginId, Capability: e.Resolve() as TContract))
                .Where(static pair => pair.Capability is not null)
                .Select(static pair => (pair.PluginId, pair.Capability!))
                .ToList();
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<Type> GetRegisteredContracts() => [.. _byContract.Keys];

    /// <summary>Registers a capability contributed by a plugin.</summary>
    /// <param name="contract">The contract type.</param>
    /// <param name="pluginId">The contributing plugin.</param>
    /// <param name="loadOrder">The plugin's load order, used to order results deterministically.</param>
    /// <param name="factory">Factory producing the capability. Invoked at most once.</param>
    public void Register(Type contract, string pluginId, int loadOrder, Func<object> factory)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(factory);

        lock (_gate)
        {
            List<Entry> entries = _byContract.GetOrAdd(contract, static _ => []);
            entries.Add(new Entry(pluginId, loadOrder, factory));
        }

        _logger.LogDebug(
            "Plugin {PluginId} registered a {Contract} capability.",
            pluginId,
            contract.Name);
    }

    /// <summary>Removes every capability contributed by a plugin.</summary>
    /// <param name="pluginId">The plugin whose contributions are removed.</param>
    public void RemoveAllFrom(string pluginId)
    {
        lock (_gate)
        {
            foreach (KeyValuePair<Type, List<Entry>> pair in _byContract)
            {
                pair.Value.RemoveAll(entry =>
                    string.Equals(entry.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
            }
        }

        _logger.LogDebug("Removed all capabilities contributed by {PluginId}.", pluginId);
    }

    /// <summary>
    /// One registration. The instance is created on first resolution rather than at registration,
    /// so a plugin that is never used never pays for constructing its capability.
    /// </summary>
    private sealed class Entry
    {
        private readonly Func<object> _factory;
        private object? _instance;

        public Entry(string pluginId, int loadOrder, Func<object> factory)
        {
            PluginId = pluginId;
            LoadOrder = loadOrder;
            _factory = factory;
        }

        public string PluginId { get; }

        public int LoadOrder { get; }

        public object Resolve() => _instance ??= _factory();
    }
}
