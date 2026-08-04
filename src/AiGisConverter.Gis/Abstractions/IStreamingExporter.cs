using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Writes features to a file without holding them all in memory.
/// </summary>
/// <remarks>
/// The contract is a stream in, a file out. Nothing in the signature permits an implementation to
/// count the features before writing, which is deliberate: a format that needs a feature count in
/// its header must buffer to a temporary file rather than to a list, and this shape makes that
/// requirement obvious rather than accidental.
/// </remarks>
public interface IStreamingExporter
{
    /// <summary>Gets the format key, for example <c>geojson</c>.</summary>
    string FormatKey { get; }

    /// <summary>Gets the export format this writer serves.</summary>
    ExportFormat Format { get; }

    /// <summary>Gets the primary file extension, including the leading dot.</summary>
    string FileExtension { get; }

    /// <summary>Gets a value indicating whether the format can hold several layers in one file.</summary>
    bool SupportsMultipleLayers { get; }

    /// <summary>Writes one layer.</summary>
    /// <param name="request">What to write and where.</param>
    /// <param name="features">The features, consumed once, lazily.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the write.</param>
    /// <returns>The paths written, or a failure describing why the export stopped.</returns>
    Task<Result<IReadOnlyList<string>>> WriteAsync(
        ExportRequest request,
        IAsyncEnumerable<GisFeature> features,
        IProgress<ExportProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What an exporter is being asked to produce.</summary>
/// <param name="OutputPath">Destination file or folder.</param>
/// <param name="FeatureClass">The layer's name and geometry family.</param>
/// <param name="Schema">The attribute schema.</param>
/// <param name="CoordinateSystem">The system the geometries are in.</param>
/// <param name="Context">The conversion context, for findings and counters.</param>
public sealed record ExportRequest(
    string OutputPath,
    FeatureClass FeatureClass,
    GisAttributeSchema Schema,
    CoordinateSystem CoordinateSystem,
    GisConversionContext Context);

/// <summary>Progress reported while exporting.</summary>
/// <param name="FeaturesWritten">How many features have been written.</param>
/// <param name="Message">Short status message.</param>
public readonly record struct ExportProgress(long FeaturesWritten, string Message);
