using AiGisConverter.Bridge.Client;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.AutoCad;

/// <summary>
/// AutoCAD Reader.
/// </summary>
/// <remarks>
/// Entities are read through the Autodesk .NET API inside AutoCAD, then returned as WKT.
/// All transport, timeout and mapping behaviour is inherited from
/// <see cref="HostBoundReaderBase"/>; only the identity of the format differs.
/// </remarks>
internal sealed class AutoCadReader : HostBoundReaderBase
{
    /// <summary>Initializes a new instance of the <see cref="AutoCadReader"/> class.</summary>
    /// <param name="bridgeClient">Client for the AutoCAD add-in.</param>
    /// <param name="logger">Logger for the reader.</param>
    public AutoCadReader(IBridgeClient bridgeClient, ILogger logger)
        : base(bridgeClient, logger)
    {
    }

    /// <inheritdoc />
    public override string FormatKey => "dwg";

    /// <inheritdoc />
    public override string DisplayName => "AutoCAD Reader";

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".dwg"];
}
