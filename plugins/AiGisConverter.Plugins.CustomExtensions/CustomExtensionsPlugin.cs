using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.CustomExtensions;

/// <summary>
/// Template plugin, and the shortest complete example of the contract.
/// </summary>
/// <remarks>
/// To build your own: copy this folder, change <c>id</c>, <c>entryAssembly</c> and
/// <c>entryType</c> in <c>plugin.json</c>, and register whatever capabilities you implement.
/// The contracts available today are <see cref="IDataSourceReader"/>,
/// <see cref="IFeatureExporter"/> and <c>IAIProvider</c>; the registration surface is open, so a
/// contract added by a later release needs no change to the SDK.
/// </remarks>
public sealed class CustomExtensionsPlugin : PluginBase
{
    /// <inheritdoc />
    public override string Id => "aigis.sample.custom";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IDataSourceReader>(new DelimitedPointReader());

        return Task.CompletedTask;
    }
}
