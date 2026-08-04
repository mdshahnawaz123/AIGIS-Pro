using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Runs the quality rules over converted datasets and produces a report.
/// </summary>
/// <remarks>
/// Distinct from <c>Domain.Validation</c>, which enforces entity invariants. Invariants are about
/// whether the software is correct; these rules are about whether the <em>data</em> is fit to
/// hand to a GIS. A geometry that self-intersects is perfectly valid as an object and entirely
/// unacceptable as a parcel boundary.
/// </remarks>
public interface IQaQcEngine
{
    /// <summary>Validates a set of datasets.</summary>
    /// <param name="runId">The run these datasets belong to.</param>
    /// <param name="datasets">The datasets to validate.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The report, or a failure when validation could not be performed at all.</returns>
    Task<Result<ValidationReport>> ValidateAsync(
        ConversionRunId runId,
        IReadOnlyList<GisDataset> datasets,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default);
}
