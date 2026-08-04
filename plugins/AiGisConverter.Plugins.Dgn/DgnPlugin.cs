using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Dgn;

/// <summary>
/// Contributes the Bentley DGN Reader to the host.
/// </summary>
public sealed class DgnPlugin : PluginBase, IPluginHealthCheck
{
    /// <inheritdoc />
    public override string Id => "aigis.reader.dgn";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IDataSourceReader>(
            _ => new DgnReader(registration.Context));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(DgnReader.IsBackendAvailable
            ? PluginHealth.Healthy("Ready.")
            : PluginHealth.Unhealthy(
                "Format backend not wired.",
                "ODA Drawings SDK or GDAL's OGR DGN driver is not yet bound in this build."));
}
