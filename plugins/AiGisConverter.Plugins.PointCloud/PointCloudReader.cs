using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.PointCloud;

/// <summary>
/// Point Cloud Reader.
/// </summary>
/// <remarks>
/// <para>
/// Format detection and the plugin contract are complete; the backend binding is not.
/// Implement <see cref="ReadAsync"/> against libE57Format or PDAL.
/// </para>
/// <para>
/// Subsample and cluster before emitting; a raw cloud is not a GIS feature set.
/// </para>
/// <para>
/// The reader returns a failed <see cref="Result"/> rather than throwing, so an unbound backend
/// shows the user one clear sentence instead of aborting a batch run.
/// </para>
/// </remarks>
internal sealed class PointCloudReader : IDataSourceReader
{
    private readonly IPluginContext _context;

    /// <summary>Initializes a new instance of the <see cref="PointCloudReader"/> class.</summary>
    /// <param name="context">The plugin context.</param>
    public PointCloudReader(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    /// Gets a value indicating whether the format backend is bound in this build.
    /// Flip to true in the same change that implements <see cref="ReadAsync"/>.
    /// </summary>
    public static bool IsBackendAvailable => false;

    /// <inheritdoc />
    public string FormatKey => "pointcloud";

    /// <inheritdoc />
    public string DisplayName => "Point Cloud Reader";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = [".e57", ".pts", ".ply", ".rcp"];

    /// <inheritdoc />
    public bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase)
               && File.Exists(reference.Location);
    }

    /// <inheritdoc />
    public Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        _context.Logger.LogWarning(
            "{Reader} recognised '{Location}' but its format backend is not bound in this build.",
            DisplayName,
            reference.Location);

        return Task.FromResult(Result.Failure<SourceDocument>(new Error(
            "Plugin.BackendNotBound",
            "The Point Cloud Reader recognises this file but its format backend is not bound in this " +
            "build. Bind libE57Format or PDAL in PointCloudReader.ReadAsync.")));
    }
}
