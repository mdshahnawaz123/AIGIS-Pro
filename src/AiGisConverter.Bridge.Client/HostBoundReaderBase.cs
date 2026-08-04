using AiGisConverter.Bridge.Protocol;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Bridge.Client;

/// <summary>
/// Base class for readers whose vendor API only functions inside a host application's own process.
/// </summary>
/// <remarks>
/// <para>
/// AutoCAD, Civil 3D and Revit all share this shape: the converter cannot call the API directly,
/// so it asks an add-in to do the work and hands back a document. Because the difference between
/// the three is only the host name and the file extensions, they are three small subclasses rather
/// than three copies of the same pipe-handling and error-mapping code.
/// </para>
/// <para>
/// Failures are returned as <see cref="Result"/> rather than thrown. "Revit is not running" is an
/// ordinary Tuesday, not an exceptional condition, and the user needs a sentence explaining what
/// to start &#8212; not a stack trace.
/// </para>
/// </remarks>
public abstract class HostBoundReaderBase : IDataSourceReader
{
    /// <summary>Initializes a new instance of the <see cref="HostBoundReaderBase"/> class.</summary>
    /// <param name="bridgeClient">Client for the host application's add-in.</param>
    /// <param name="logger">Logger for the reader.</param>
    protected HostBoundReaderBase(IBridgeClient bridgeClient, ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(bridgeClient);
        ArgumentNullException.ThrowIfNull(logger);

        BridgeClient = bridgeClient;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string FormatKey { get; }

    /// <inheritdoc />
    public abstract string DisplayName { get; }

    /// <inheritdoc />
    public abstract IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Gets the bridge client.</summary>
    protected IBridgeClient BridgeClient { get; }

    /// <summary>Gets the logger.</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public virtual bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase);
    }

    /// <inheritdoc />
    public async Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        progress?.Report(new ReadProgress(0d, $"Contacting the {BridgeClient.HostName} add-in..."));

        IReadOnlyDictionary<string, string>? handshake =
            await BridgeClient.HandshakeAsync(cancellationToken).ConfigureAwait(false);

        if (handshake is null)
        {
            return Result.Failure<SourceDocument>(new Error(
                "Bridge.HostUnavailable",
                $"{BridgeClient.HostName} is not running, or the AI GIS Converter add-in is not loaded. " +
                $"Start {BridgeClient.HostName} and try again."));
        }

        progress?.Report(new ReadProgress(0.1d, $"Reading via {BridgeClient.HostName}..."));

        BridgeRequest request = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Method = BridgeMethods.ReadDocument,
            Location = reference.Location,
        };

        foreach (KeyValuePair<string, string> hint in reference.Hints)
        {
            request.Arguments[hint.Key] = hint.Value;
        }

        BridgeResponse response;

        try
        {
            response = await BridgeClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (TimeoutException ex)
        {
            return Result.Failure<SourceDocument>(new Error("Bridge.Timeout", ex.Message));
        }
        catch (IOException ex)
        {
            return Result.Failure<SourceDocument>(new Error(
                "Bridge.TransportFailure",
                $"The connection to {BridgeClient.HostName} failed: {ex.Message}"));
        }

        if (!response.Success || response.Document is null)
        {
            return Result.Failure<SourceDocument>(new Error(
                "Bridge.ReadFailed",
                response.Error ?? $"The {BridgeClient.HostName} add-in returned no document."));
        }

        progress?.Report(new ReadProgress(0.9d, "Mapping geometry..."));

        SourceDocument document = BridgeDocumentMapper.ToSourceDocument(response.Document, reference);

        Logger.LogInformation(
            "Read {ElementCount} elements across {LayerCount} layers from {Location} via {HostName}.",
            document.CountElements(),
            document.Layers.Count,
            reference.Location,
            BridgeClient.HostName);

        progress?.Report(new ReadProgress(1d, "Done."));

        return Result.Success(document);
    }
}
