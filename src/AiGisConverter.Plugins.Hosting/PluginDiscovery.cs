using System.Text.Json;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>
/// Default <see cref="IPluginDiscovery"/>. Walks each search path, reads every
/// <c>plugin.json</c>, and decides &#8212; from the manifest alone &#8212; whether the plugin is a
/// candidate for loading.
/// </summary>
/// <remarks>
/// Nothing here loads an assembly. Deciding compatibility before mapping code into the process is
/// the difference between "this plugin targets an older SDK" and an unrecoverable
/// <see cref="BadImageFormatException"/> during startup.
/// </remarks>
public sealed class PluginDiscovery : IPluginDiscovery
{
    private static readonly JsonSerializerOptions ManifestOptions = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly IOptionsMonitor<PluginOptions> _options;
    private readonly ILogger<PluginDiscovery> _logger;

    /// <summary>Initializes a new instance of the <see cref="PluginDiscovery"/> class.</summary>
    /// <param name="options">Live plugin options.</param>
    /// <param name="logger">Logger for discovery diagnostics.</param>
    public PluginDiscovery(IOptionsMonitor<PluginOptions> options, ILogger<PluginDiscovery> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PluginDescriptor>> DiscoverAsync(
        CancellationToken cancellationToken = default)
    {
        PluginOptions options = _options.CurrentValue;
        List<PluginDescriptor> descriptors = [];
        HashSet<string> seenIds = new(StringComparer.OrdinalIgnoreCase);

        // Logged at Information, not Debug. When discovery finds nothing there is no other evidence
        // of why, and the default minimum level means a Debug line is written nowhere at all - so
        // the one diagnostic that mattered was the one nobody could see.
        _logger.LogInformation(
            "Plugin discovery starting. BaseDirectory '{BaseDirectory}', {PathCount} configured search path(s): {SearchPaths}",
            AppContext.BaseDirectory,
            options.SearchPaths.Count,
            string.Join(" | ", options.SearchPaths));

        foreach (string searchPath in options.SearchPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string root = ResolveSearchPath(searchPath);
            bool exists = Directory.Exists(root);

            _logger.LogInformation(
                "Search path '{SearchPath}' resolved to '{ResolvedPath}' (exists: {Exists}).",
                searchPath,
                root,
                exists);

            if (!exists)
            {
                continue;
            }

            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string manifestPath = Path.Combine(directory, PluginManifest.FileName);
                bool hasManifest = File.Exists(manifestPath);

                _logger.LogInformation(
                    "Examining plugin folder '{Directory}' ({FileName} present: {HasManifest}).",
                    directory,
                    PluginManifest.FileName,
                    hasManifest);

                if (!hasManifest)
                {
                    continue;
                }

                PluginDescriptor? descriptor =
                    await ReadDescriptorAsync(manifestPath, directory, cancellationToken).ConfigureAwait(false);

                if (descriptor is null)
                {
                    continue;
                }

                if (!seenIds.Add(descriptor.Id))
                {
                    descriptor.State = PluginLoadState.Rejected;
                    descriptor.FailureReason =
                        $"A plugin with id '{descriptor.Id}' was already found in an earlier search path.";
                    _logger.LogWarning(
                        "Duplicate plugin id '{PluginId}' at '{Directory}' was skipped.",
                        descriptor.Id,
                        directory);
                }
                else
                {
                    Validate(descriptor, options);
                }

                descriptors.Add(descriptor);
            }
        }

        _logger.LogInformation(
            "Plugin discovery found {Total} plugins ({Candidates} candidates for loading).",
            descriptors.Count,
            descriptors.Count(d => d.State == PluginLoadState.Discovered));

        return descriptors;
    }

    private async Task<PluginDescriptor?> ReadDescriptorAsync(
        string manifestPath,
        string directory,
        CancellationToken cancellationToken)
    {
        try
        {
            await using FileStream stream = File.OpenRead(manifestPath);

            PluginManifest? manifest = await JsonSerializer
                .DeserializeAsync<PluginManifest>(stream, ManifestOptions, cancellationToken)
                .ConfigureAwait(false);

            if (manifest is null || string.IsNullOrWhiteSpace(manifest.Id))
            {
                _logger.LogWarning("Manifest '{ManifestPath}' is empty or has no id.", manifestPath);
                return null;
            }

            _logger.LogInformation(
                "Manifest '{ManifestPath}' loaded: id '{Id}' v{Version}, sdk {SdkVersion}, isolation {Isolation}, entry '{EntryAssembly}', capabilities [{Capabilities}].",
                manifestPath,
                manifest.Id,
                manifest.Version,
                manifest.SdkVersion,
                manifest.Isolation,
                manifest.EntryAssembly,
                string.Join(", ", manifest.Capabilities));

            return new PluginDescriptor(manifest, directory);
        }
        catch (JsonException ex)
        {
            // The message and path matter as much as the type: a manifest that fails to parse is
            // skipped silently by design, and without this the plugin simply never appears.
            _logger.LogError(
                ex,
                "Manifest '{ManifestPath}' could not be deserialised and the plugin was skipped: {Message}",
                manifestPath,
                ex.Message);

            return null;
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Manifest '{ManifestPath}' could not be read: {Message}", manifestPath, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            // Nothing here should throw anything else, which is exactly why an unexpected exception
            // must be recorded rather than allowed to end discovery without explanation.
            _logger.LogError(
                ex,
                "Manifest '{ManifestPath}' failed unexpectedly ({Type}): {Message}",
                manifestPath,
                ex.GetType().Name,
                ex.Message);

            return null;
        }
    }

    /// <summary>Applies every pre-load gate, recording the first failure.</summary>
    private void Validate(PluginDescriptor descriptor, PluginOptions options)
    {
        PluginManifest manifest = descriptor.Manifest;

        // Every path out of this method sets State and FailureReason; logging the outcome here once
        // means no gate can reject a plugin without saying so.
        void Reject(PluginLoadState state, string reason)
        {
            descriptor.State = state;
            descriptor.FailureReason = reason;

            _logger.LogWarning(
                "Plugin {PluginId} will not be loaded ({State}): {Reason}",
                descriptor.Id,
                state,
                reason);
        }

        if (!manifest.Enabled)
        {
            Reject(PluginLoadState.Disabled, "Disabled in its own manifest.");
            return;
        }

        if (options.Enabled.Count > 0 &&
            !options.Enabled.Contains(descriptor.Id, StringComparer.OrdinalIgnoreCase))
        {
            Reject(PluginLoadState.Disabled, "Not in the configured 'Plugins:Enabled' allowlist.");
            return;
        }

        if (options.Disabled.Contains(descriptor.Id, StringComparer.OrdinalIgnoreCase))
        {
            Reject(PluginLoadState.Disabled, "Listed in 'Plugins:Disabled'.");
            return;
        }

        if (!PluginSdk.IsCompatible(manifest.SdkVersion, out string reason))
        {
            Reject(PluginLoadState.Rejected, reason);
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            Reject(PluginLoadState.Rejected, "The manifest does not name an 'entryAssembly'.");
            return;
        }

        if (!File.Exists(descriptor.GetEntryAssemblyPath()))
        {
            Reject(
                PluginLoadState.Rejected,
                $"Entry assembly '{manifest.EntryAssembly}' was not found at '{descriptor.GetEntryAssemblyPath()}'.");
        }
    }

    /// <summary>Expands environment variables and resolves relative paths against the application folder.</summary>
    private static string ResolveSearchPath(string searchPath)
    {
        string expanded = Environment.ExpandEnvironmentVariables(searchPath);

        return Path.IsPathRooted(expanded)
            ? expanded
            : Path.Combine(AppContext.BaseDirectory, expanded);
    }
}
