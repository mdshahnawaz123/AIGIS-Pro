using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Hosting;

namespace AiGisConverter.Composition;

/// <summary>
/// Presents built-in exporters and plugin-contributed exporters as one catalogue.
/// </summary>
public sealed class PluginAwareFeatureExporterCatalog : IFeatureExporterCatalog
{
    private readonly IEnumerable<IFeatureExporter> _builtIn;
    private readonly ICapabilityRegistry _registry;

    /// <summary>Initializes a new instance of the <see cref="PluginAwareFeatureExporterCatalog"/> class.</summary>
    /// <param name="builtIn">Exporters registered directly with the container.</param>
    /// <param name="registry">The host capability registry.</param>
    public PluginAwareFeatureExporterCatalog(
        IEnumerable<IFeatureExporter> builtIn,
        ICapabilityRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        ArgumentNullException.ThrowIfNull(registry);

        _builtIn = builtIn;
        _registry = registry;
    }

    /// <inheritdoc />
    public IReadOnlyList<IFeatureExporter> GetExporters() =>
        [.. _builtIn, .. _registry.GetCapabilities<IFeatureExporter>()];

    /// <inheritdoc />
    public IFeatureExporter? FindExporter(string formatKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(formatKey);

        return GetExporters()
            .FirstOrDefault(exporter =>
                string.Equals(exporter.FormatKey, formatKey, StringComparison.OrdinalIgnoreCase));
    }
}
