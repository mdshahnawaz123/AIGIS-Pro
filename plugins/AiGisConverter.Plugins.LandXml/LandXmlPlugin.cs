using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.LandXml;

/// <summary>
/// Contributes the LandXML Reader to the host.
/// </summary>
public sealed class LandXmlPlugin : PluginBase, IPluginHealthCheck
{
    /// <inheritdoc />
    public override string Id => "aigis.reader.landxml";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IDataSourceReader>(
            _ => new LandXmlReader(registration.Context));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Always healthy: LandXML is read with the .NET XML stack, so unlike the SDK-bound readers
    /// there is no backend that can be missing on a given machine.
    /// </remarks>
    public Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(PluginHealth.Healthy("Ready."));
}
