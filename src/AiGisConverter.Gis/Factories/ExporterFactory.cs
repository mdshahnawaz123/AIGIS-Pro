using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;

namespace AiGisConverter.Gis.Factories;

/// <summary>Resolves the exporter for a format.</summary>
public interface IExporterFactory
{
    /// <summary>Gets every registered exporter.</summary>
    IReadOnlyList<IStreamingExporter> Exporters { get; }

    /// <summary>Resolves by format key, for example <c>geopackage</c>.</summary>
    /// <param name="formatKey">The format key, matched case-insensitively.</param>
    /// <returns>The exporter, or a failure naming what is available.</returns>
    Result<IStreamingExporter> Resolve(string formatKey);

    /// <summary>Resolves by export format.</summary>
    /// <param name="format">The format.</param>
    /// <returns>The exporter, or a failure naming what is available.</returns>
    Result<IStreamingExporter> Resolve(ExportFormat format);
}

/// <summary>
/// Default <see cref="IExporterFactory"/>.
/// </summary>
/// <remarks>
/// Indexes whatever is registered; it contains no list of formats of its own. Adding a writer is a
/// container registration, and an unresolvable key names the alternatives rather than throwing a
/// bare key-not-found, because a format typo in a profile is the likeliest cause.
/// </remarks>
public sealed class ExporterFactory : IExporterFactory
{
    private readonly Dictionary<string, IStreamingExporter> _byKey;

    /// <summary>Initializes a new instance of the <see cref="ExporterFactory"/> class.</summary>
    /// <param name="exporters">Every registered exporter.</param>
    public ExporterFactory(IEnumerable<IStreamingExporter> exporters)
    {
        ArgumentNullException.ThrowIfNull(exporters);

        Exporters = [.. exporters];
        _byKey = new Dictionary<string, IStreamingExporter>(StringComparer.OrdinalIgnoreCase);

        foreach (IStreamingExporter exporter in Exporters)
        {
            _byKey.TryAdd(exporter.FormatKey, exporter);
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<IStreamingExporter> Exporters { get; }

    /// <inheritdoc />
    public Result<IStreamingExporter> Resolve(string formatKey)
    {
        if (string.IsNullOrWhiteSpace(formatKey))
        {
            return Result.Failure<IStreamingExporter>(new Error(
                "Gis.FormatNotSpecified",
                "No export format was named."));
        }

        return _byKey.TryGetValue(formatKey.Trim(), out IStreamingExporter? exporter)
            ? Result.Success(exporter)
            : Result.Failure<IStreamingExporter>(new Error(
                "Gis.FormatNotSupported",
                $"'{formatKey}' is not a supported export format. Available: {string.Join(", ", _byKey.Keys.Order(StringComparer.Ordinal))}."));
    }

    /// <inheritdoc />
    public Result<IStreamingExporter> Resolve(ExportFormat format)
    {
        IStreamingExporter? exporter = Exporters.FirstOrDefault(e => e.Format == format);

        return exporter is not null
            ? Result.Success(exporter)
            : Result.Failure<IStreamingExporter>(new Error(
                "Gis.FormatNotSupported",
                $"No exporter is registered for {format}."));
    }
}
