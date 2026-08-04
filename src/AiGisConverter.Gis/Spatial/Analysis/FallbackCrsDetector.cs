using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Gis.Spatial.Analysis;

/// <summary>
/// A fallback CRS detector for Version 1.0 that defaults to WGS 84.
/// </summary>
public sealed class FallbackCrsDetector : ICrsDetector
{
    /// <inheritdoc />
    public Task<Result<CrsDetectionResult>> DetectAsync(
        SourceDocument document,
        CoordinateSystem? assumedSystem = null,
        CancellationToken cancellationToken = default)
    {
        CoordinateSystem system = assumedSystem ?? CoordinateSystem.Wgs84;
        
        return Task.FromResult(Result.Success(
            new CrsDetectionResult(system, CrsDetectionSource.ApplicationDefault, Confidence.Certain)));
    }
}
