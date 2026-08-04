using System.Collections.Generic;

namespace AiGisConverter.Bridge.Protocol
{
    /// <summary>A single request sent to an add-in bridge server.</summary>
    public sealed class BridgeRequest
    {
        /// <summary>Gets or sets the correlation identifier echoed in the response.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets the method name. See <see cref="BridgeMethods"/>.</summary>
        public string Method { get; set; } = string.Empty;

        /// <summary>Gets or sets the protocol version the client speaks.</summary>
        public string ProtocolVersion { get; set; } = BridgeProtocol.Version;

        /// <summary>Gets or sets the source location the request concerns, when applicable.</summary>
        public string Location { get; set; }

        /// <summary>Gets or sets free-form method arguments.</summary>
        public Dictionary<string, string> Arguments { get; set; } = new Dictionary<string, string>();
    }
}
