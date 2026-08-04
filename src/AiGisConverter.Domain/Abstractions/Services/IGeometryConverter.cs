using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Turns a read source document into export-ready GIS datasets.
/// </summary>
/// <remarks>
/// This is where curve tessellation, hatch-to-polygon conversion and geometry repair happen. The
/// domain states the transformation it needs; the GIS layer performs it, because the work requires
/// NetTopologySuite operations the domain has no business knowing about.
/// </remarks>
public interface IGeometryConverter
{
    /// <summary>Converts a document into one dataset per feature class.</summary>
    /// <param name="document">The document to convert.</param>
    /// <param name="classification">The feature class assigned to each source layer, keyed by layer name.</param>
    /// <param name="sourceSystem">The system the document's coordinates are in.</param>
    /// <param name="targetSystem">The system the output should be in.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The datasets, or a failure describing why conversion could not complete.</returns>
    Task<Result<IReadOnlyList<GisDataset>>> ConvertAsync(
        SourceDocument document,
        IReadOnlyDictionary<string, AiGisConverter.Domain.Entities.Ai.ClassificationResult> classification,
        CoordinateSystem sourceSystem,
        CoordinateSystem targetSystem,
        CancellationToken cancellationToken = default);
}
