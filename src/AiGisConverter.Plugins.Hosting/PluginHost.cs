using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// Default <see cref="IPluginHost"/>.
/// </summary>
/// <remarks>
/// <para>
/// Load failures are contained. A plugin that throws during construction or configuration is
/// marked failed, its context is released, and loading continues with the next one. On a machine
/// where Revit is not installed, the Revit plugin fails and the application still opens.
/// </para>
/// <para>
/// Unloading is best-effort by nature. A collectible context is only released once nothing
/// references anything inside it, and the host cannot force that. The descriptor is therefore
/// marked unloaded once the plugin has been shut down and its capabilities withdrawn; whether the
/// context is physically collected is reported separately in the log.
/// </para>
/// </remarks>
public sealed class PluginHost : IPluginHost, IAsyncDisposable
{
    private readonly IPluginDiscovery _discovery;
    private readonly CapabilityRegistry _registry;
    private readonly IOptionsMonitor<PluginOptions> _options;
    private readonly IConfiguration _configuration;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<PluginHost> _logger;
    private readonly ConcurrentDictionary<string, LoadedPlugin> _loaded = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _shutdown = new();
    private readonly List<PluginDescriptor> _descriptors = [];

    /// <summary>Initializes a new instance of the <see cref="PluginHost"/> class.</summary>
    /// <param name="discovery">Finds plugins on disk.</param>
    /// <param name="registry">Receives contributed capabilities.</param>
    /// <param name="options">Live plugin options.</param>
    /// <param name="configuration">Application configuration, used to build each plugin's section.</param>
    /// <param name="loggerFactory">Creates a logger per plugin.</param>
    /// <param name="logger">Logger for the host.</param>
    internal PluginHost(
        IPluginDiscovery discovery,
        CapabilityRegistry registry,
        IOptionsMonitor<PluginOptions> options,
        IConfiguration configuration,
        ILoggerFactory loggerFactory,
        ILogger<PluginHost> logger)
    {
        ArgumentNullException.ThrowIfNull(discovery);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(logger);

        _discovery = discovery;
        _registry = registry;
        _options = options;
        _configuration = configuration;
        _loggerFactory = loggerFactory;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<PluginDescriptor> Plugins => _descriptors;

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginDescriptor>> LoadAllAsync(
        CancellationToken cancellationToken = default)
    {
        PluginOptions options = _options.CurrentValue;
        IReadOnlyList<PluginDescriptor> discovered =
            await _discovery.DiscoverAsync(cancellationToken).ConfigureAwait(false);

        _descriptors.Clear();
        _descriptors.AddRange(discovered);

        foreach (PluginDescriptor descriptor in discovered
            .Where(static d => d.State == PluginLoadState.Discovered)
            .OrderBy(static d => d.Manifest.LoadOrder)
            .ThenBy(static d => d.Id, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await LoadOneAsync(descriptor, options, cancellationToken).ConfigureAwait(false);
        }

        _logger.LogInformation(
            "Loaded {Loaded} of {Total} plugins.",
            _descriptors.Count(static d => d.State == PluginLoadState.Loaded),
            _descriptors.Count);

        return _descriptors;
    }

    /// <inheritdoc />
    public async Task<bool> UnloadAsync(string pluginId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginId);

        if (!_loaded.TryRemove(pluginId, out LoadedPlugin? plugin))
        {
            return false;
        }

        _registry.RemoveAllFrom(pluginId);
        await plugin.DisposeAsync().ConfigureAwait(false);

        plugin.Descriptor.State = PluginLoadState.Unloaded;

        if (plugin.LoadContext is not null)
        {
            ReleaseContext(plugin.LoadContext, pluginId);
        }

        _logger.LogInformation("Unloaded plugin {PluginId}.", pluginId);
        return true;
    }

    /// <inheritdoc />
    public async Task UnloadAllAsync(CancellationToken cancellationToken = default)
    {
        await _shutdown.CancelAsync().ConfigureAwait(false);

        foreach (string id in _loaded.Keys.ToList())
        {
            await UnloadAsync(id, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await UnloadAllAsync(CancellationToken.None).ConfigureAwait(false);
        _shutdown.Dispose();
    }

    private async Task LoadOneAsync(
        PluginDescriptor descriptor,
        PluginOptions options,
        CancellationToken cancellationToken)
    {
        long startedAt = Stopwatch.GetTimestamp();
        AssemblyLoadContext? loadContext = null;

        using CancellationTokenSource timeout =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(options.LoadTimeoutSeconds));

        try
        {
            Assembly assembly = LoadEntryAssembly(descriptor, options, out loadContext);
            Type pluginType = ResolvePluginType(descriptor, assembly);

            if (Activator.CreateInstance(pluginType) is not IPlugin instance)
            {
                throw new PluginException(descriptor.Id, $"'{pluginType.FullName}' is not an {nameof(IPlugin)}.");
            }

            PluginContext context = new(
                descriptor.Manifest,
                descriptor.Directory,
                _configuration.GetSection($"{PluginOptions.SectionName}:{descriptor.Id}"),
                _loggerFactory.CreateLogger($"Plugin.{descriptor.Id}"),
                _loggerFactory,
                GetHostVersion(),
                _shutdown.Token);

            PluginRegistrationContext registration = new(context, _registry);

            await instance.ConfigureAsync(registration, timeout.Token).ConfigureAwait(false);
            registration.Publish(descriptor.Id, descriptor.Manifest.LoadOrder);

            _loaded[descriptor.Id] = new LoadedPlugin(descriptor, instance, registration, loadContext);

            descriptor.State = PluginLoadState.Loaded;
            descriptor.LoadDuration = Stopwatch.GetElapsedTime(startedAt);

            _logger.LogInformation(
                "Loaded plugin {PluginId} v{Version} in {ElapsedMs} ms.",
                descriptor.Id,
                descriptor.Manifest.Version,
                descriptor.LoadDuration.TotalMilliseconds);
        }
        catch (Exception ex) when (ex is PluginException
                                      or BadImageFormatException
                                      or FileLoadException
                                      or FileNotFoundException
                                      or TypeLoadException
                                      or MissingMethodException
                                      or TargetInvocationException
                                      or OperationCanceledException)
        {
            descriptor.State = PluginLoadState.Failed;
            descriptor.FailureReason = ex.Message;
            descriptor.LoadDuration = Stopwatch.GetElapsedTime(startedAt);

            _logger.LogError(
                ex,
                "Plugin {PluginId} failed to load from '{Directory}'.",
                descriptor.Id,
                descriptor.Directory);

            if (loadContext is not null)
            {
                ReleaseContext(loadContext, descriptor.Id);
            }

            if (options.FailFastOnLoadError)
            {
                throw new PluginException(descriptor.Id, $"Plugin '{descriptor.Id}' failed to load.", ex);
            }
        }
    }

    /// <summary>Loads the entry assembly into an isolated context, or into the host's own.</summary>
    private static Assembly LoadEntryAssembly(
        PluginDescriptor descriptor,
        PluginOptions options,
        out AssemblyLoadContext? loadContext)
    {
        string path = descriptor.GetEntryAssemblyPath();

        if (descriptor.Manifest.Isolation == PluginIsolationMode.Shared)
        {
            loadContext = null;
            return Assembly.LoadFrom(path);
        }

        List<string> shared = [.. options.SharedAssemblies, .. descriptor.Manifest.SharedAssemblies];
        PluginLoadContext context = new($"Plugin:{descriptor.Id}", path, shared);

        loadContext = context;
        return context.LoadFromAssemblyPath(path);
    }

    /// <summary>Finds the plugin type, either by the manifest's declared name or by scanning.</summary>
    private static Type ResolvePluginType(PluginDescriptor descriptor, Assembly assembly)
    {
        if (!string.IsNullOrWhiteSpace(descriptor.Manifest.EntryType))
        {
            return assembly.GetType(descriptor.Manifest.EntryType, throwOnError: false)
                   ?? throw new PluginException(
                       descriptor.Id,
                       $"Entry type '{descriptor.Manifest.EntryType}' was not found in '{assembly.GetName().Name}'.");
        }

        Type[] candidates = assembly
            .GetExportedTypes()
            .Where(static t => typeof(IPlugin).IsAssignableFrom(t) && t is { IsAbstract: false, IsInterface: false })
            .ToArray();

        return candidates.Length switch
        {
            1 => candidates[0],
            0 => throw new PluginException(
                descriptor.Id,
                $"'{assembly.GetName().Name}' contains no public {nameof(IPlugin)} implementation."),
            _ => throw new PluginException(
                descriptor.Id,
                $"'{assembly.GetName().Name}' contains {candidates.Length} {nameof(IPlugin)} implementations. " +
                "Name one explicitly with 'entryType' in the manifest."),
        };
    }

    /// <summary>
    /// Requests unload and reports whether the runtime actually collected the context.
    /// </summary>
    /// <remarks>
    /// A context stays alive while anything still references a type inside it &#8212; an event
    /// handler, a cached delegate, a static field. Reporting the outcome turns a silent leak into
    /// a log line that names the plugin responsible.
    /// </remarks>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void ReleaseContext(AssemblyLoadContext context, string pluginId)
    {
        WeakReference reference = new(context, trackResurrection: true);
        context.Unload();

        for (int attempt = 0; attempt < 10 && reference.IsAlive; attempt++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        if (reference.IsAlive)
        {
            _logger.LogWarning(
                "The load context for plugin {PluginId} was not collected. Something still holds a " +
                "reference into it, so its assemblies remain mapped until the process exits.",
                pluginId);
        }
    }

    private static Version GetHostVersion() =>
        Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0, 0);
}
