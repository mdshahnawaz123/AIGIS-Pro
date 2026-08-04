using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Ifc;

/// <summary>
/// Contributes the IFC Reader to the host.
/// </summary>
public sealed class IfcPlugin : PluginBase, IPluginHealthCheck
{
    /// <inheritdoc />
    public override string Id => "aigis.reader.ifc";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IDataSourceReader>(
            _ => new IfcReader(registration.Context));
        registration.AddCapability<ISemanticProvider>(new IfcSemanticProvider());
        registration.AddCapability<IPluginCapabilities>(new IfcCapabilities());
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(IfcReader.IsBackendAvailable
            ? PluginHealth.Healthy("Ready.")
            : PluginHealth.Unhealthy(
                "Format backend not wired.",
                "xBIM or IfcOpenShell is not yet bound in this build."));
}
