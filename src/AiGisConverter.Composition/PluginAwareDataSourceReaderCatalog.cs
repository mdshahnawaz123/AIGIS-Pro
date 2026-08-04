using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Plugins.Hosting;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Composition;

/// <summary>
/// Presents built-in readers and plugin-contributed readers as one catalogue.
/// </summary>
/// <remarks>
/// Built-in readers win ties. A plugin claiming an extension the host already handles is used only
/// when no built-in reader accepts the file, so installing a plugin can add formats but cannot
/// silently take over an existing one.
/// </remarks>
public sealed class PluginAwareDataSourceReaderCatalog : IDataSourceReaderCatalog
{
    private readonly IEnumerable<IDataSourceReader> _builtIn;
    private readonly ICapabilityRegistry _registry;
    private readonly ILogger<PluginAwareDataSourceReaderCatalog> _logger;

    /// <summary>Initializes a new instance of the <see cref="PluginAwareDataSourceReaderCatalog"/> class.</summary>
    /// <param name="builtIn">Readers registered directly with the container.</param>
    /// <param name="registry">The host capability registry.</param>
    /// <param name="logger">Logger for resolution diagnostics.</param>
    public PluginAwareDataSourceReaderCatalog(
        IEnumerable<IDataSourceReader> builtIn,
        ICapabilityRegistry registry,
        ILogger<PluginAwareDataSourceReaderCatalog> logger)
    {
        ArgumentNullException.ThrowIfNull(builtIn);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(logger);

        _builtIn = builtIn;
        _registry = registry;
        _logger = logger;
    }

    /// <inheritdoc />
    public IReadOnlyList<IDataSourceReader> GetReaders() =>
        [.. _builtIn, .. _registry.GetCapabilities<IDataSourceReader>()];

    /// <inheritdoc />
    public IDataSourceReader? FindReader(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        foreach (IDataSourceReader reader in GetReaders())
        {
            if (!reader.CanRead(reference))
            {
                continue;
            }

            _logger.LogDebug(
                "Reader {FormatKey} claimed '{Location}'.",
                reader.FormatKey,
                reference.Location);

            return reader;
        }

        _logger.LogWarning("No reader claimed '{Location}'.", reference.Location);
        return null;
    }

    /// <inheritdoc />
    public IReadOnlyList<string> GetSupportedExtensions() =>
        GetReaders()
            .SelectMany(static reader => reader.SupportedExtensions)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static extension => extension, StringComparer.Ordinal)
            .ToList();
}
