using System.Text.Json;

namespace AiGisConverter.Bridge.Protocol
{
    /// <summary>
    /// Version and serialisation settings shared by both sides of the bridge.
    /// </summary>
    public static class BridgeProtocol
    {
        /// <summary>The wire protocol version.</summary>
        public const string Version = "1.0";

        /// <summary>The default named pipe name template. The token is replaced by the host name.</summary>
        public const string PipeNameTemplate = "AiGisConverter.Bridge.{0}";

        /// <summary>Serialisation settings. Both sides must use these or the contract silently drifts.</summary>
        public static JsonSerializerOptions SerializerOptions { get; } = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        /// <summary>Builds the pipe name for a host application.</summary>
        /// <param name="hostName">Host application name, for example <c>Revit</c>.</param>
        /// <returns>The pipe name.</returns>
        public static string GetPipeName(string hostName)
        {
            return string.Format(System.Globalization.CultureInfo.InvariantCulture, PipeNameTemplate, hostName);
        }
    }
}
