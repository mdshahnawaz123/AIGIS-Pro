using AiGisConverter.Bridge.Client;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.Civil3D;

/// <summary>
/// Civil 3D Reader.
/// </summary>
/// <remarks>
/// Adds alignments, profiles, corridors and surfaces, which the plain AutoCAD API does not expose.
/// All transport, timeout and mapping behaviour is inherited from
/// <see cref="HostBoundReaderBase"/>; only the identity of the format differs.
/// </remarks>
internal sealed class Civil3DReader : HostBoundReaderBase
{
    /// <summary>Initializes a new instance of the <see cref="Civil3DReader"/> class.</summary>
    /// <param name="bridgeClient">Client for the Civil3D add-in.</param>
    /// <param name="logger">Logger for the reader.</param>
    public Civil3DReader(IBridgeClient bridgeClient, ILogger logger)
        : base(bridgeClient, logger)
    {
    }

    /// <inheritdoc />
    public override string FormatKey => "civil3d";

    /// <inheritdoc />
    public override string DisplayName => "Civil 3D Reader";

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".dwg"];
}
