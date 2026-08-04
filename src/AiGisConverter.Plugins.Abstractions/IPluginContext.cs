using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// Everything the host gives a plugin about its own environment.
/// </summary>
/// <remarks>
/// A plugin receives its world through this interface rather than reaching for
/// <c>AppContext.BaseDirectory</c>, a static logger or a global configuration singleton. That is
/// what makes a plugin testable outside the host and unloadable inside it.
/// </remarks>
public interface IPluginContext
{
    /// <summary>Gets the plugin's manifest.</summary>
    PluginManifest Manifest { get; }

    /// <summary>Gets the folder the plugin was loaded from. Treat as read-only.</summary>
    string PluginDirectory { get; }

    /// <summary>
    /// Gets a writable folder reserved for this plugin, under the user's application data.
    /// Created on first access. Plugins must not write into <see cref="PluginDirectory"/>,
    /// which may be installed under Program Files.
    /// </summary>
    string DataDirectory { get; }

    /// <summary>Gets the plugin's own configuration section, bound from <c>Plugins:&lt;id&gt;</c>.</summary>
    IConfiguration Configuration { get; }

    /// <summary>Gets a logger scoped to this plugin.</summary>
    ILogger Logger { get; }

    /// <summary>
    /// Gets the logger factory, so a plugin can create the typed loggers its own dependencies ask
    /// for without reaching for a static logger or inventing a second logging configuration.
    /// </summary>
    ILoggerFactory LoggerFactory { get; }

    /// <summary>Gets the host application version, so a plugin can adapt to older hosts.</summary>
    Version HostVersion { get; }

    /// <summary>Gets a token cancelled when the host begins shutting down.</summary>
    CancellationToken ShutdownToken { get; }
}
