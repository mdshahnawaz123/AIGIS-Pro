namespace AiGisConverter.Bridge.Protocol
{
    /// <summary>
    /// The method names understood by an add-in bridge server.
    /// </summary>
    /// <remarks>
    /// The protocol is deliberately tiny. Everything expensive &#8212; geometry, attributes &#8212;
    /// travels in the document payload of a single <see cref="ReadDocument"/> response rather than
    /// through chatty per-element calls, because each round trip crosses a process boundary and,
    /// for Revit, must be marshalled onto the host's UI thread.
    /// </remarks>
    public static class BridgeMethods
    {
        /// <summary>Returns the add-in version and the host application release.</summary>
        public const string Handshake = "handshake";

        /// <summary>Returns whether the add-in can read the supplied reference.</summary>
        public const string CanRead = "canRead";

        /// <summary>Reads a document and returns it as a <see cref="BridgeDocument"/>.</summary>
        public const string ReadDocument = "readDocument";

        /// <summary>Lists the documents currently open in the host application.</summary>
        public const string ListOpenDocuments = "listOpenDocuments";

        /// <summary>Asks the add-in to shut down its listener.</summary>
        public const string Shutdown = "shutdown";
    }
}
