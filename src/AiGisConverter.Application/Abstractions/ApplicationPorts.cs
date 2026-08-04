using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Application.Abstractions;

/// <summary>
/// Writes converted datasets to disk.
/// </summary>
/// <remarks>
/// Declared here rather than used directly from the GIS layer because this project references only
/// Domain. The domain's own <c>IFeatureExporter</c> writes a source document; by this point in the
/// pipeline the data is a set of <see cref="GisDataset"/>, which is a different shape. The
/// composition root adapts one to the other, and neither layer learns about the other.
/// </remarks>
public interface IDatasetExportService
{
    /// <summary>Writes datasets.</summary>
    /// <param name="datasets">The datasets to write.</param>
    /// <param name="request">Where and how to write them.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the export.</param>
    /// <returns>The paths written, or a failure describing why the export stopped.</returns>
    Task<Result<IReadOnlyList<string>>> ExportAsync(
        IReadOnlyList<GisDataset> datasets,
        DatasetExportRequest request,
        IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>What an export is being asked to produce.</summary>
/// <param name="OutputDirectory">Folder the datasets are written into.</param>
/// <param name="ProfileId">Conversion profile to apply, or null for the configured default.</param>
/// <param name="FormatKey">Export format key, overriding the profile.</param>
public sealed record DatasetExportRequest(
    string OutputDirectory,
    string? ProfileId = null,
    string? FormatKey = null);

/// <summary>Renders a validation report to disk.</summary>
/// <remarks>
/// The QA/QC layer owns rendering. This port exists so the pipeline can ask for it without this
/// project referencing that one.
/// </remarks>
public interface IQaReportRenderer
{
    /// <summary>Renders a report in every configured format.</summary>
    /// <param name="report">The report to render.</param>
    /// <param name="outputPathWithoutExtension">Destination path, without an extension.</param>
    /// <param name="cancellationToken">Token used to cancel rendering.</param>
    /// <returns>The paths written.</returns>
    Task<IReadOnlyList<string>> RenderAsync(
        ValidationReport report,
        string outputPathWithoutExtension,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Tells the operator something happened.
/// </summary>
/// <remarks>
/// Deliberately not an event bus. A conversion produces a handful of notable moments and the user
/// interface wants to show them; anything richer belongs in the domain events the unit of work
/// already dispatches.
/// </remarks>
public interface INotificationService
{
    /// <summary>Raised when a notification is published.</summary>
    event EventHandler<Notification>? Published;

    /// <summary>Publishes a notification.</summary>
    /// <param name="notification">What happened.</param>
    void Publish(Notification notification);

    /// <summary>Gets the notifications published in this session, newest first.</summary>
    /// <param name="limit">Maximum number to return.</param>
    /// <returns>The recent notifications.</returns>
    IReadOnlyList<Notification> GetRecent(int limit = 50);
}

/// <summary>Something the operator should know about.</summary>
/// <param name="Level">How much it matters.</param>
/// <param name="Title">One line, shown in a toast or a list.</param>
/// <param name="Detail">Optional elaboration.</param>
/// <param name="RaisedAtUtc">When it happened.</param>
public sealed record Notification(
    NotificationLevel Level,
    string Title,
    string? Detail = null,
    DateTimeOffset? RaisedAtUtc = null)
{
    /// <summary>Gets the instant the notification was raised.</summary>
    public DateTimeOffset Timestamp => RaisedAtUtc ?? DateTimeOffset.UtcNow;
}

/// <summary>How much a notification matters.</summary>
public enum NotificationLevel
{
    /// <summary>Progress or completion.</summary>
    Information = 0,

    /// <summary>Finished, but the operator should look at something.</summary>
    Warning = 1,

    /// <summary>Did not finish.</summary>
    Error = 2,
}
