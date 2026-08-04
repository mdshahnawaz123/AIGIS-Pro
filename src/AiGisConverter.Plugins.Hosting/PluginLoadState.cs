namespace AiGisConverter.Plugins.Hosting;

/// <summary>Lifecycle state of a discovered plugin.</summary>
public enum PluginLoadState
{
    /// <summary>Found on disk, manifest read, not yet loaded.</summary>
    Discovered = 0,

    /// <summary>Excluded by configuration or by the manifest's own <c>enabled</c> flag.</summary>
    Disabled = 1,

    /// <summary>Rejected before loading: incompatible SDK, missing entry assembly, bad manifest.</summary>
    Rejected = 2,

    /// <summary>Assembly loaded and capabilities registered.</summary>
    Loaded = 3,

    /// <summary>Loading was attempted and failed.</summary>
    Failed = 4,

    /// <summary>Unloaded; its load context has been released.</summary>
    Unloaded = 5,
}
