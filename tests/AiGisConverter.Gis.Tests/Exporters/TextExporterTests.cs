using System.Buffers.Binary;
using System.Xml.Linq;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Exporters.Csv;
using AiGisConverter.Gis.Exporters.Kml;
using AiGisConverter.Gis.Exporters.Wkb;
using AiGisConverter.Gis.Exporters.Wkt;
using AiGisConverter.Gis.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Gis.Tests.Exporters;

public sealed class TextExporterTests
{
    private static async IAsyncEnumerable<GisFeature> Features(int count = 2)
    {
        for (int i = 0; i < count; i++)
        {
            yield return FeatureFactory.Polygon($"f{i}", i * 20d, 0d);
            await Task.Yield();
        }
    }

    private static ICrsRegistry StubRegistry(bool available = true)
    {
        ICrsRegistry registry = Substitute.For<ICrsRegistry>();
        registry.GetWellKnownText(Arg.Any<CoordinateSystem>())
            .Returns(available
                ? Domain.Common.Result.Success("GEOGCS[\"WGS 84\"]")
                : Domain.Common.Result.Failure<string>(new Domain.Common.Error("x", "y")));

        return registry;
    }

    [Fact]
    public async Task Csv_QuotesTheWktColumn()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("out.csv");

        StreamingCsvExporter exporter = new(GisOptionsFactory.Monitor(), NullLogger<StreamingCsvExporter>.Instance);
        await exporter.WriteAsync(FeatureFactory.Request(path), Features(1));

        string[] lines = await File.ReadAllLinesAsync(path);

        lines[0].Should().StartWith("id,WKT,");
        lines[1].Should().Contain("\"POLYGON",
            "WKT always contains commas, and an unquoted geometry column shifts every field after it");
    }

    [Fact]
    public async Task Csv_WritesABomSoExcelReadsUtf8()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("bom.csv");

        StreamingCsvExporter exporter = new(GisOptionsFactory.Monitor(), NullLogger<StreamingCsvExporter>.Instance);
        await exporter.WriteAsync(FeatureFactory.Request(path), Features(1));

        byte[] head = (await File.ReadAllBytesAsync(path))[..3];

        head.Should().Equal([0xEF, 0xBB, 0xBF]);
    }

    [Fact]
    public async Task Csv_RowCountMatchesFeatureCount()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("rows.csv");

        StreamingCsvExporter exporter = new(GisOptionsFactory.Monitor(), NullLogger<StreamingCsvExporter>.Instance);
        await exporter.WriteAsync(FeatureFactory.Request(path), Features(37));

        (await File.ReadAllLinesAsync(path)).Should().HaveCount(38, "one header plus one row per feature");
    }

    [Fact]
    public async Task Kml_ProducesWellFormedKml22()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("out.kml");

        StreamingKmlExporter exporter = new(GisOptionsFactory.Monitor(), NullLogger<StreamingKmlExporter>.Instance);
        await exporter.WriteAsync(FeatureFactory.Request(path), Features(3));

        XDocument document = XDocument.Load(path);
        XNamespace kml = "http://www.opengis.net/kml/2.2";

        document.Root!.Name.Should().Be(kml + "kml");
        document.Descendants(kml + "Placemark").Should().HaveCount(3);
        document.Descendants(kml + "Polygon").Should().HaveCount(3);
    }

    [Fact]
    public async Task Kml_NonWgs84_RecordsACriticalFinding()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("projected.kml");

        ExportRequest request = FeatureFactory.Request(path, CoordinateSystem.Create("EPSG", 27700));

        StreamingKmlExporter exporter = new(GisOptionsFactory.Monitor(), NullLogger<StreamingKmlExporter>.Instance);
        await exporter.WriteAsync(request, Features(1));

        // KML opens without error in the wrong place, which is why this has to be loud.
        request.Context.Issues.Should().Contain(i => i.Code == "Export.KmlRequiresWgs84");
    }

    [Fact]
    public async Task Wkt_WritesOneGeometryPerLineAndAProjectionFile()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("out.wkt");

        StreamingWktExporter exporter = new(
            GisOptionsFactory.Monitor(), StubRegistry(), NullLogger<StreamingWktExporter>.Instance);

        await exporter.WriteAsync(FeatureFactory.Request(path), Features(4));

        (await File.ReadAllLinesAsync(path)).Should().HaveCount(4);
        File.Exists(Path.ChangeExtension(path, ".prj")).Should().BeTrue();
    }

    [Fact]
    public async Task Wkb_FramesEachRecordWithItsLength()
    {
        using TempWorkspace workspace = new();
        string path = workspace.Path("out.wkb");

        StreamingWkbExporter exporter = new(
            GisOptionsFactory.Monitor(), StubRegistry(), NullLogger<StreamingWkbExporter>.Instance);

        await exporter.WriteAsync(FeatureFactory.Request(path), Features(3));

        byte[] bytes = await File.ReadAllBytesAsync(path);
        int offset = 0;
        int records = 0;

        while (offset + 4 <= bytes.Length)
        {
            int length = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(offset, 4));
            length.Should().BePositive();
            offset += 4 + length;
            records++;
        }

        records.Should().Be(3);
        offset.Should().Be(bytes.Length, "the framing must consume the file exactly");
    }
}
