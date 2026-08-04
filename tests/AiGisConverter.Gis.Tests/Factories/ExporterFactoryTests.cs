using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Factories;

namespace AiGisConverter.Gis.Tests.Factories;

public sealed class ExporterFactoryTests
{
    private static IStreamingExporter Exporter(string key, ExportFormat format)
    {
        IStreamingExporter exporter = Substitute.For<IStreamingExporter>();
        exporter.FormatKey.Returns(key);
        exporter.Format.Returns(format);

        return exporter;
    }

    [Theory]
    [InlineData("geojson")]
    [InlineData("GeoJSON")]
    [InlineData(" geojson ")]
    public void Resolve_ByKey_IsCaseAndWhitespaceInsensitive(string key)
    {
        ExporterFactory factory = new([Exporter("geojson", ExportFormat.GeoJson)]);

        factory.Resolve(key).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Resolve_UnknownKey_NamesWhatIsAvailable()
    {
        ExporterFactory factory = new(
        [
            Exporter("geojson", ExportFormat.GeoJson),
            Exporter("geopackage", ExportFormat.GeoPackage),
        ]);

        Result<IStreamingExporter> result = factory.Resolve("shapefil");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("geojson").And.Contain("geopackage");
    }

    [Fact]
    public void Resolve_ByFormat_Works()
    {
        ExporterFactory factory = new([Exporter("gpkg", ExportFormat.GeoPackage)]);

        factory.Resolve(ExportFormat.GeoPackage).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void Resolve_BlankKey_Fails() =>
        new ExporterFactory([]).Resolve("  ").IsFailure.Should().BeTrue();

    [Fact]
    public void Constructor_DuplicateKeys_KeepsTheFirst()
    {
        IStreamingExporter first = Exporter("geojson", ExportFormat.GeoJson);
        ExporterFactory factory = new([first, Exporter("geojson", ExportFormat.GeoJson)]);

        factory.Resolve("geojson").Value.Should().BeSameAs(first);
    }
}
