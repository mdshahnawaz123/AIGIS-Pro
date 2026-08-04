namespace AiGisConverter.Plugins.Abstractions;

/// <summary>
/// Optional interface a plugin may implement so the plugin manager can show whether its
/// prerequisites are actually satisfied &#8212; a licence, an installed CAD release, a reachable
/// bridge &#8212; rather than only whether it loaded.
/// </summary>
public interface IPluginHealthCheck
{
    /// <summary>Checks the plugin's prerequisites.</summary>
    /// <param name="cancellationToken">Token used to cancel the check.</param>
    /// <returns>The health of the plugin.</returns>
    Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default);
}

/// <summary>The outcome of a plugin health check.</summary>
/// <param name="IsHealthy">Whether the plugin's prerequisites are satisfied.</param>
/// <param name="Summary">One line suitable for the plugin manager list.</param>
/// <param name="Detail">Optional detail, for example the resolved host application path.</param>
public sealed record PluginHealth(bool IsHealthy, string Summary, string? Detail = null)
{
    /// <summary>Creates a healthy result.</summary>
    /// <param name="summary">One-line summary.</param>
    /// <param name="detail">Optional detail.</param>
    /// <returns>A healthy <see cref="PluginHealth"/>.</returns>
    public static PluginHealth Healthy(string summary, string? detail = null) => new(true, summary, detail);

    /// <summary>Creates an unhealthy result.</summary>
    /// <param name="summary">One-line summary.</param>
    /// <param name="detail">Optional detail.</param>
    /// <returns>An unhealthy <see cref="PluginHealth"/>.</returns>
    public static PluginHealth Unhealthy(string summary, string? detail = null) => new(false, summary, detail);
}
