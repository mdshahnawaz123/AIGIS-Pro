using AiGisConverter.Bridge.Client;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Civil3D;

/// <summary>
/// Contributes the Civil 3D Reader to the host.
/// </summary>
/// <remarks>
/// Registration always succeeds, even when Civil3D is absent. Whether Civil3D is actually
/// reachable is a question for <see cref="CheckHealthAsync"/> and for the moment a read is
/// attempted &#8212; not for load time, because Civil3D may be started after the converter.
/// </remarks>
public sealed class Civil3DPlugin : PluginBase, IPluginHealthCheck
{
    private const string HostName = "Civil3D";
    private const int ConnectTimeoutMilliseconds = 2000;

    private IBridgeClient? _bridgeClient;

    /// <inheritdoc />
    public override string Id => "aigis.reader.civil3d";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        IPluginContext context = registration.Context;

        _bridgeClient = new NamedPipeBridgeClient(
            HostName,
            context.Manifest.HostApplication?.PipeName,
            ConnectTimeoutMilliseconds,
            context.Logger);

        registration.AddCapability<IDataSourceReader>(new Civil3DReader(_bridgeClient, context.Logger));
        registration.AddCapability<ISemanticProvider>(new Civil3DSemanticProvider());
        registration.AddCapability<IPluginCapabilities>(new Civil3DCapabilities());

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default)
    {
        if (_bridgeClient is null)
        {
            return PluginHealth.Unhealthy("Not configured.");
        }

        IReadOnlyDictionary<string, string>? handshake =
            await _bridgeClient.HandshakeAsync(cancellationToken).ConfigureAwait(false);

        if (handshake is null)
        {
            return PluginHealth.Unhealthy(
                $"{HostName} is not running, or the add-in is not installed.",
                "Install the AI GIS Converter add-in and start " + HostName + ".");
        }

        string version = handshake.TryGetValue("hostVersion", out string? reported) && reported is not null
            ? reported
            : "(version unknown)";

        return PluginHealth.Healthy($"Connected to {HostName} {version}.");
    }
}
