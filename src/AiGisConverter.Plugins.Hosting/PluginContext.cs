using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.Hosting;

/// <summary>Default <see cref="IPluginContext"/>.</summary>
internal sealed class PluginContext : IPluginContext
{
    private readonly Lazy<string> _dataDirectory;

    public PluginContext(
        PluginManifest manifest,
        string pluginDirectory,
        IConfiguration configuration,
        ILogger logger,
        ILoggerFactory loggerFactory,
        Version hostVersion,
        CancellationToken shutdownToken)
    {
        Manifest = manifest;
        PluginDirectory = pluginDirectory;
        Configuration = configuration;
        Logger = logger;
        LoggerFactory = loggerFactory;
        HostVersion = hostVersion;
        ShutdownToken = shutdownToken;

        _dataDirectory = new Lazy<string>(CreateDataDirectory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public PluginManifest Manifest { get; }

    public string PluginDirectory { get; }

    public string DataDirectory => _dataDirectory.Value;

    public IConfiguration Configuration { get; }

    public ILogger Logger { get; }

    public ILoggerFactory LoggerFactory { get; }

    public Version HostVersion { get; }

    public CancellationToken ShutdownToken { get; }

    /// <summary>
    /// Creates the plugin's private data folder under the user's application data, never under the
    /// installation folder, which is typically not writable.
    /// </summary>
    private string CreateDataDirectory()
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiGisConverter",
            "PluginData",
            SanitiseId(Manifest.Id));

        Directory.CreateDirectory(path);
        return path;
    }

    private static string SanitiseId(string id)
    {
        char[] invalid = Path.GetInvalidFileNameChars();
        Span<char> buffer = stackalloc char[id.Length];

        for (int i = 0; i < id.Length; i++)
        {
            buffer[i] = Array.IndexOf(invalid, id[i]) >= 0 ? '_' : id[i];
        }

        return new string(buffer);
    }
}
