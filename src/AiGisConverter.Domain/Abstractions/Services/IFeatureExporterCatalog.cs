namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// The set of exporters currently available, whether built in or contributed by a plugin.
/// </summary>
public interface IFeatureExporterCatalog
{
    /// <summary>Gets every available exporter.</summary>
    /// <returns>The exporters, in a deterministic order.</returns>
    IReadOnlyList<IFeatureExporter> GetExporters();

    /// <summary>Finds an exporter by its format key.</summary>
    /// <param name="formatKey">The format key, matched case-insensitively.</param>
    /// <returns>The exporter, or <see langword="null"/> when no exporter offers that format.</returns>
    IFeatureExporter? FindExporter(string formatKey);
}
