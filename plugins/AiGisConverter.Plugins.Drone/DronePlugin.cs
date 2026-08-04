using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Drone;

/// <summary>
/// Contributes the Drone Survey Reader to the host.
/// </summary>
public sealed class DronePlugin : PluginBase, IPluginHealthCheck
{
    /// <inheritdoc />
    public override string Id => "aigis.reader.drone";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IDataSourceReader>(
            _ => new DroneReader(registration.Context));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DroneReader.IsBackendAvailable
            ? PluginHealth.Healthy("Ready.")
            : PluginHealth.Unhealthy(
                "Format backend not wired.",
                "GDAL for orthophotos, plus an EXIF reader for flight metadata is not yet bound in this build."));
}
