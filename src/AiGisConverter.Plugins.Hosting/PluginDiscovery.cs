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

        foreach (string searchPath in options.SearchPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string root = ResolveSearchPath(searchPath);

            if (!Directory.Exists(root))
            {
                _logger.LogDebug("Plugin search path '{SearchPath}' does not exist.", root);
                continue;
            }

            foreach (string directory in Directory.EnumerateDirectories(root))
            {
                cancellationToken.ThrowIfCancellationRequested();

                string manifestPath = Path.Combine(directory, PluginManifest.FileName);

                if (!File.Exists(manifestPath))
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

            return new PluginDescriptor(manifest, directory);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Manifest '{ManifestPath}' is not valid JSON.", manifestPath);
            return null;
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "Manifest '{ManifestPath}' could not be read.", manifestPath);
            return null;
        }
    }

    /// <summary>Applies every pre-load gate, recording the first failure.</summary>
    private void Validate(PluginDescriptor descriptor, PluginOptions options)
    {
        PluginManifest manifest = descriptor.Manifest;

        if (!manifest.Enabled)
        {
            descriptor.State = PluginLoadState.Disabled;
            descriptor.FailureReason = "Disabled in its own manifest.";
            return;
        }

        if (options.Enabled.Count > 0 &&
            !options.Enabled.Contains(descriptor.Id, StringComparer.OrdinalIgnoreCase))
        {
            descriptor.State = PluginLoadState.Disabled;
            descriptor.FailureReason = "Not in the configured 'Plugins:Enabled' allowlist.";
            return;
        }

        if (options.Disabled.Contains(descriptor.Id, StringComparer.OrdinalIgnoreCase))
        {
            descriptor.State = PluginLoadState.Disabled;
            descriptor.FailureReason = "Listed in 'Plugins:Disabled'.";
            return;
        }

        if (!PluginSdk.IsCompatible(manifest.SdkVersion, out string reason))
        {
            descriptor.State = PluginLoadState.Rejected;
            descriptor.FailureReason = reason;
            _logger.LogWarning("Plugin {PluginId} rejected: {Reason}", descriptor.Id, reason);
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.EntryAssembly))
        {
            descriptor.State = PluginLoadState.Rejected;
            descriptor.FailureReason = "The manifest does not name an 'entryAssembly'.";
            return;
        }

        if (!File.Exists(descriptor.GetEntryAssemblyPath()))
        {
            descriptor.State = PluginLoadState.Rejected;
            descriptor.FailureReason =
                $"Entry assembly '{manifest.EntryAssembly}' was not found in the plugin folder.";
            _logger.LogWarning(
                "Plugin {PluginId} rejected: {Reason}",
                descriptor.Id,
                descriptor.FailureReason);
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
