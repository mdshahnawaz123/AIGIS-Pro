using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.GisExport;

/// <summary>
/// Contributes GIS output formats to the host.
/// </summary>
/// <remarks>
/// One plugin may contribute several capabilities of the same contract. Additional writers
/// &#8212; File Geodatabase, FlatGeobuf, PostGIS &#8212; are added by registering another
/// <see cref="IFeatureExporter"/> here, with no change anywhere else in the application.
/// </remarks>
public sealed class GisExportPlugin : PluginBase
{
    /// <inheritdoc />
    public override string Id => "aigis.export.gis";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IFeatureExporter>(new GeoJsonExporter());

        return Task.CompletedTask;
    }
}
