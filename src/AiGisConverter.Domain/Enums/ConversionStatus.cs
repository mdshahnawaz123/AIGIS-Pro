namespace AiGisConverter.Domain.Enums;

/// <summary>Lifecycle state of a conversion job or run.</summary>
public enum ConversionStatus
{
    /// <summary>Created but not yet queued.</summary>
    Draft = 0,

    /// <summary>Queued and waiting for a worker.</summary>
    Queued = 1,

    /// <summary>Currently executing.</summary>
    Running = 2,

    /// <summary>Finished, with every stage successful.</summary>
    Succeeded = 3,

    /// <summary>
    /// Finished and produced output, but with findings the operator should read.
    /// Distinguished from <see cref="Succeeded"/> so a batch summary can say honestly that
    /// something needs looking at without claiming the run failed.
    /// </summary>
    SucceededWithWarnings = 4,

    /// <summary>Finished without usable output.</summary>
    Failed = 5,

    /// <summary>Stopped at the operator's request.</summary>
    Cancelled = 6,
}
