using AiGisConverter.Bridge.Protocol;

namespace AiGisConverter.Bridge.Client;

/// <summary>
/// Talks to an add-in running inside a host CAD application.
/// </summary>
public interface IBridgeClient
{
    /// <summary>Gets the host application name this client targets.</summary>
    string HostName { get; }

    /// <summary>Checks whether the add-in is listening.</summary>
    /// <param name="cancellationToken">Token used to cancel the probe.</param>
    /// <returns>The handshake values, or null when the add-in is not reachable.</returns>
    Task<IReadOnlyDictionary<string, string>?> HandshakeAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a request and waits for the response.</summary>
    /// <param name="request">The request to send.</param>
    /// <param name="cancellationToken">Token used to cancel the call.</param>
    /// <returns>The response.</returns>
    Task<BridgeResponse> SendAsync(BridgeRequest request, CancellationToken cancellationToken = default);
}
