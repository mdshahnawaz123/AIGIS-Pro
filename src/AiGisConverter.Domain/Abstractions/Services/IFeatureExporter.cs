using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Driven port for writing a converted document to a GIS format.
/// </summary>
/// <remarks>
/// Implemented by the built-in exporters and by export plugins. A plugin that adds, say, an Esri
/// File Geodatabase writer contributes another implementation and nothing else changes.
/// </remarks>
public interface IFeatureExporter
{
    /// <summary>Gets the exporter's format key, for example <c>geojson</c> or <c>gpkg</c>.</summary>
    string FormatKey { get; }

    /// <summary>Gets the human-readable format name shown in the export dialog.</summary>
    string DisplayName { get; }

    /// <summary>Gets the primary file extension written, including the leading dot.</summary>
    string FileExtension { get; }

    /// <summary>Writes a document.</summary>
    /// <param name="document">The document to write.</param>
    /// <param name="outputPath">Destination file or folder path.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The paths written, or a failure describing why the export did not complete.</returns>
    Task<Result<IReadOnlyList<string>>> ExportAsync(
        SourceDocument document,
        string outputPath,
        CancellationToken cancellationToken = default);
}
