using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Lidar;

/// <summary>
/// Contributes the LiDAR Reader to the host.
/// </summary>
public sealed class LidarPlugin : PluginBase, IPluginHealthCheck
{
    /// <inheritdoc />
    public override string Id => "aigis.reader.lidar";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IDataSourceReader>(
            _ => new LidarReader(registration.Context));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(LidarReader.IsBackendAvailable
            ? PluginHealth.Healthy("Ready.")
            : PluginHealth.Unhealthy(
                "Format backend not wired.",
                "LASzip or PDAL is not yet bound in this build."));
}
