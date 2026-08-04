using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Events;

/// <summary>Raised when a run begins.</summary>
/// <param name="RunId">The run.</param>
/// <param name="JobId">The job being executed.</param>
public sealed record ConversionRunStarted(ConversionRunId RunId, ConversionJobId JobId) : DomainEvent;

/// <summary>
/// Raised once the coordinate reference system has been settled.
/// </summary>
/// <remarks>
/// Carries the <paramref name="Source"/> as well as the system, because when a survey ends up in
/// the wrong place the useful question is not which CRS was used but how it was decided.
/// </remarks>
/// <param name="RunId">The run.</param>
/// <param name="CoordinateSystem">The system that will be used.</param>
/// <param name="Source">How it was determined.</param>
public sealed record CoordinateSystemDetermined(
    ConversionRunId RunId,
    CoordinateSystem CoordinateSystem,
    CrsDetectionSource Source) : DomainEvent;

/// <summary>Raised once classification has finished for a run.</summary>
/// <param name="RunId">The run.</param>
/// <param name="AcceptedCount">Layers classified at or above the confidence threshold.</param>
/// <param name="BelowThresholdCount">Layers classified but below the threshold, needing review.</param>
/// <param name="UnclassifiedCount">Layers no provider could classify.</param>
public sealed record LayersClassified(
    ConversionRunId RunId,
    int AcceptedCount,
    int BelowThresholdCount,
    int UnclassifiedCount) : DomainEvent;

/// <summary>Raised once validation has finished for a run.</summary>
/// <param name="RunId">The run.</param>
/// <param name="HighestSeverity">The most serious finding.</param>
/// <param name="IssueCount">Total findings recorded.</param>
public sealed record ValidationCompleted(
    ConversionRunId RunId,
    IssueSeverity HighestSeverity,
    int IssueCount) : DomainEvent;

/// <summary>Raised when a run finishes, whatever the outcome.</summary>
/// <param name="RunId">The run.</param>
/// <param name="JobId">The job that was executed.</param>
/// <param name="Status">The terminal status.</param>
/// <param name="Duration">Wall-clock duration.</param>
/// <param name="FeatureCount">Features written.</param>
public sealed record ConversionRunFinished(
    ConversionRunId RunId,
    ConversionJobId JobId,
    ConversionStatus Status,
    TimeSpan Duration,
    int FeatureCount) : DomainEvent;
