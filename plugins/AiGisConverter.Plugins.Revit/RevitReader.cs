using AiGisConverter.Bridge.Client;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.Revit;

/// <summary>
/// Revit Reader.
/// </summary>
/// <remarks>
/// Elements are read on Revit's UI thread inside an ExternalEvent; the add-in serialises the result.
/// All transport, timeout and mapping behaviour is inherited from
/// <see cref="HostBoundReaderBase"/>; only the identity of the format differs.
/// </remarks>
internal sealed class RevitReader : HostBoundReaderBase
{
    /// <summary>Initializes a new instance of the <see cref="RevitReader"/> class.</summary>
    /// <param name="bridgeClient">Client for the Revit add-in.</param>
    /// <param name="logger">Logger for the reader.</param>
    public RevitReader(IBridgeClient bridgeClient, ILogger logger)
        : base(bridgeClient, logger)
    {
    }

    /// <inheritdoc />
    public override string FormatKey => "rvt";

    /// <inheritdoc />
    public override string DisplayName => "Revit Reader";

    /// <inheritdoc />
    public override IReadOnlyList<string> SupportedExtensions { get; } = [".rvt", ".rfa"];
}
