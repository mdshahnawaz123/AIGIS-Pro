using System.Collections.Generic;

namespace AiGisConverter.Bridge.Protocol
{
    /// <summary>A single response returned by an add-in bridge server.</summary>
    public sealed class BridgeResponse
    {
        /// <summary>Gets or sets the correlation identifier from the request.</summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>Gets or sets a value indicating whether the call succeeded.</summary>
        public bool Success { get; set; }

        /// <summary>Gets or sets the failure message when <see cref="Success"/> is false.</summary>
        public string Error { get; set; }

        /// <summary>Gets or sets the document payload, for a read request.</summary>
        public BridgeDocument Document { get; set; }

        /// <summary>Gets or sets scalar return values, for the simpler methods.</summary>
        public Dictionary<string, string> Values { get; set; } = new Dictionary<string, string>();

        /// <summary>Creates a failed response.</summary>
        /// <param name="id">Correlation identifier.</param>
        /// <param name="error">Failure message.</param>
        /// <returns>A failed <see cref="BridgeResponse"/>.</returns>
        public static BridgeResponse Failed(string id, string error)
        {
            return new BridgeResponse { Id = id, Success = false, Error = error };
        }
    }
}
