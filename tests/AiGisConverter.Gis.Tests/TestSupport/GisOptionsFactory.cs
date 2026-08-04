using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Tests.TestSupport;

/// <summary>Builds an options monitor over a concrete <see cref="GisOptions"/> for tests.</summary>
internal static class GisOptionsFactory
{
    public static IOptionsMonitor<GisOptions> Monitor(Action<GisOptions>? configure = null)
    {
        GisOptions options = new();
        configure?.Invoke(options);

        IOptionsMonitor<GisOptions> monitor = Substitute.For<IOptionsMonitor<GisOptions>>();
        monitor.CurrentValue.Returns(options);

        return monitor;
    }
}
