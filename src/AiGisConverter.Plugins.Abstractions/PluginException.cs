namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// Raised when a plugin cannot be discovered, validated, loaded or initialised.
/// </summary>
public class PluginException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="PluginException"/> class.</summary>
    public PluginException()
        : this("The plugin operation failed.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PluginException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public PluginException(string message)
        : base(message) => PluginId = "unknown";

    /// <summary>Initializes a new instance of the <see cref="PluginException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public PluginException(string message, Exception innerException)
        : base(message, innerException) => PluginId = "unknown";

    /// <summary>Initializes a new instance of the <see cref="PluginException"/> class.</summary>
    /// <param name="pluginId">Identifier of the plugin concerned.</param>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    public PluginException(string pluginId, string message, Exception? innerException = null)
        : base(message, innerException) => PluginId = pluginId;

    /// <summary>Gets the identifier of the plugin concerned.</summary>
    public string PluginId { get; }
}
