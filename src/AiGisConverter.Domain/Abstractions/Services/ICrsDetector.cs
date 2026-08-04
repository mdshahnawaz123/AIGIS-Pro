using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Determines what coordinate reference system a source document is in.
/// </summary>
/// <remarks>
/// Detection is a chain of decreasingly reliable strategies, and the result records which one
/// answered. That provenance is the single most useful piece of metadata when data lands in the
/// wrong place, so it is part of the contract rather than a log line.
/// </remarks>
public interface ICrsDetector
{
    /// <summary>Detects the coordinate system of a document.</summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="assumedSystem">The system to fall back to, or null to require detection to succeed.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The detection outcome, or a failure when no system could be determined.</returns>
    Task<Result<CrsDetectionResult>> DetectAsync(
        SourceDocument document,
        CoordinateSystem? assumedSystem = null,
        CancellationToken cancellationToken = default);
}

/// <summary>The outcome of coordinate system detection.</summary>
/// <param name="CoordinateSystem">The system that will be used.</param>
/// <param name="Source">Which strategy supplied it.</param>
/// <param name="Confidence">How much to trust it, for the heuristic strategies.</param>
public sealed record CrsDetectionResult(
    CoordinateSystem CoordinateSystem,
    CrsDetectionSource Source,
    Confidence Confidence);
