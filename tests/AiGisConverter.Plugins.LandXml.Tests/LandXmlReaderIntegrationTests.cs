using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Plugins.LandXml.Tests;

/// <summary>
/// End-to-end tests: a real LandXML document parsed through the reader.
/// </summary>
/// <remarks>
/// The document below is a complete, schema-shaped LandXML file exercising every collection the
/// reader claims to support. Asserting against a real file rather than mocks is the only way to
/// catch the failures that actually occur — a namespace mismatch, a collection nested one level
/// deeper than expected, a coordinate order silently transposed.
/// </remarks>
public sealed class LandXmlReaderIntegrationTests : IDisposable
{
    private const string Sample = """
        <?xml version="1.0" encoding="UTF-8"?>
        <LandXML xmlns="http://www.landxml.org/schema/LandXML-1.2" version="1.2">
          <Units><Metric linearUnit="meter" areaUnit="squareMeter"/></Units>
          <CoordinateSystem name="UTM Zone 40N" epsgCode="32640" horizontalDatum="WGS84"/>
          <CgPoints name="Survey">
            <CgPoint name="1" code="TREE" desc="Palm">2762100.000 483200.000 12.500</CgPoint>
            <CgPoint name="2" code="MH">2762150.000 483250.000 13.250</CgPoint>
          </CgPoints>
          <Parcels>
            <Parcel name="LOT-1" area="10000.00" parcelType="Single" state="proposed" owner="Expo City">
              <CoordGeom>
                <Line><Start>2762000.000 483000.000</Start><End>2762000.000 483100.000</End></Line>
                <Line><Start>2762000.000 483100.000</Start><End>2762100.000 483100.000</End></Line>
                <Line><Start>2762100.000 483100.000</Start><End>2762100.000 483000.000</End></Line>
                <Line><Start>2762100.000 483000.000</Start><End>2762000.000 483000.000</End></Line>
              </CoordGeom>
            </Parcel>
          </Parcels>
          <Alignments>
            <Alignment name="RD-01" length="150.000" staStart="0.000" desc="Access road">
              <CoordGeom>
                <Line><Start>2762200.000 483000.000</Start><End>2762200.000 483150.000</End></Line>
              </CoordGeom>
            </Alignment>
          </Alignments>
          <Surfaces>
            <Surface name="EG" desc="Existing ground">
              <SourceData>
                <Breaklines>
                  <Breakline name="BL-1" brkType="standard">
                    <PntList3D>2762000.0 483000.0 10.0 2762050.0 483050.0 11.0 2762100.0 483100.0 12.0</PntList3D>
                  </Breakline>
                </Breaklines>
                <Boundaries>
                  <Boundary name="OUTER" bndType="outer" edgeTrim="true">
                    <PntList3D>2762000.0 483000.0 10.0 2762000.0 483100.0 10.0 2762100.0 483100.0 12.0 2762100.0 483000.0 11.0</PntList3D>
                  </Boundary>
                </Boundaries>
              </SourceData>
              <Definition surfType="TIN">
                <Pnts>
                  <P id="1">2762000.000 483000.000 10.000</P>
                  <P id="2">2762000.000 483100.000 10.000</P>
                  <P id="3">2762100.000 483100.000 12.000</P>
                  <P id="4">2762100.000 483000.000 11.000</P>
                </Pnts>
                <Faces>
                  <F n="2 0 0">1 2 3</F>
                  <F n="1 0 0">1 3 4</F>
                </Faces>
              </Definition>
            </Surface>
          </Surfaces>
          <PlanFeatures name="Planimetrics">
            <PlanFeature name="FENCE-1" desc="Boundary fence">
              <CoordGeom>
                <Line><Start>2762300.000 483000.000</Start><End>2762300.000 483080.000</End></Line>
              </CoordGeom>
            </PlanFeature>
          </PlanFeatures>
          <PipeNetworks>
            <PipeNetwork name="STORM">
              <Structs>
                <Struct name="MH-1" elevRim="14.000" elevSump="11.000" desc="Manhole">
                  <Center>2762400.000 483000.000 14.000</Center>
                </Struct>
                <Struct name="MH-2" elevRim="13.500" elevSump="10.500" desc="Manhole">
                  <Center>2762450.000 483000.000 13.500</Center>
                </Struct>
              </Structs>
              <Pipes>
                <Pipe name="P-1" refStart="MH-1" refEnd="MH-2" material="Concrete" slope="0.010" flowDir="Start to End">
                  <CircPipe diameter="0.450" thickness="0.050"/>
                </Pipe>
              </Pipes>
            </PipeNetwork>
          </PipeNetworks>
        </LandXML>
        """;

    private readonly string _path = Path.Combine(
        Path.GetTempPath(), $"aigis-landxml-{Guid.NewGuid():N}.landxml");

    public LandXmlReaderIntegrationTests() => File.WriteAllText(_path, Sample);

    public void Dispose()
    {
        try
        {
            File.Delete(_path);
        }
        catch (IOException)
        {
        }
    }

    private static IPluginContext Context()
    {
        IPluginContext context = Substitute.For<IPluginContext>();
        context.Logger.Returns(NullLogger.Instance);

        return context;
    }

    private async Task<SourceDocument> ReadAsync()
    {
        LandXmlReader reader = new(Context());
        Result<SourceDocument> result = await reader.ReadAsync(new SourceReference(_path));

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "the sample must parse");

        return result.Value;
    }

    [Fact]
    public void CanRead_ClaimsLandXml_ByItsRootElement()
    {
        new LandXmlReader(Context()).CanRead(new SourceReference(_path)).Should().BeTrue();
    }

    [Fact]
    public void CanRead_RejectsAnUnrelatedXmlFile()
    {
        string other = Path.Combine(Path.GetTempPath(), $"aigis-not-landxml-{Guid.NewGuid():N}.xml");
        File.WriteAllText(other, "<?xml version=\"1.0\"?><Project><Item/></Project>");

        try
        {
            // ".xml" alone is far too broad a claim; the root element must decide.
            new LandXmlReader(Context()).CanRead(new SourceReference(other)).Should().BeFalse();
        }
        finally
        {
            File.Delete(other);
        }
    }

    [Fact]
    public async Task Crs_IsPropagated_FromTheCoordinateSystemElement()
    {
        SourceDocument document = await ReadAsync();

        document.DeclaredCrs.Should().Be("EPSG:32640",
            "an EPSG code in the file must reach CRS detection unchanged");
        document.Units.Should().Be("meter");
        document.Metadata.Should().ContainKey("HorizontalDatum");
    }

    [Fact]
    public async Task EveryCollection_ProducesItsOwnLayer()
    {
        SourceDocument document = await ReadAsync();

        document.Layers.Select(static layer => layer.Name).Should().Contain(
            ["CgPoints:Survey", "Parcels", "Alignments", "Surface:EG", "Breaklines:EG", "Boundaries:EG",
             "PlanFeatures:Planimetrics", "Structures:STORM", "Pipes:STORM"]);
    }

    [Fact]
    public async Task Geometry_IsValid_ForEveryElementThatHasIt()
    {
        SourceDocument document = await ReadAsync();

        IReadOnlyList<SourceElement> elements =
            [.. document.Layers.SelectMany(static layer => layer.Elements)];

        elements.Should().NotBeEmpty();
        elements.Where(static e => e.Geometry is not null)
            .Should().OnlyContain(e => e.Geometry!.IsValid, "invalid geometry fails later, in the exporter");
    }

    [Fact]
    public async Task Coordinates_AreEastingFirst_NotTransposed()
    {
        SourceDocument document = await ReadAsync();

        SourceElement point = document.Layers
            .Single(static layer => layer.Name == "CgPoints:Survey")
            .Elements.First();

        // The file says "2762100 483200": northing then easting.
        point.Geometry!.Coordinate.X.Should().BeApproximately(483200d, 1e-6d, "x is the easting");
        point.Geometry!.Coordinate.Y.Should().BeApproximately(2762100d, 1e-6d, "y is the northing");
    }

    [Fact]
    public async Task Parcel_ClosesIntoAPolygonWithTheSurveyedArea()
    {
        SourceDocument document = await ReadAsync();

        SourceElement parcel = document.Layers
            .Single(static layer => layer.Name == "Parcels")
            .Elements.Single();

        parcel.GeometryKind.Should().Be(GeometryKind.Polygon);
        parcel.Geometry!.Area.Should().BeApproximately(10_000d, 1e-3d,
            "a 100 by 100 metre lot encloses ten thousand square metres");
        parcel.Attributes.Should().ContainKey("Owner");
    }

    [Fact]
    public async Task Surface_PublishesOneRealPolygonPerTriangle()
    {
        SourceDocument document = await ReadAsync();

        IReadOnlyList<SourceElement> faces =
            [.. document.Layers.Single(static layer => layer.Name == "Surface:EG").Elements];

        faces.Should().HaveCount(2, "the sample TIN has two faces");
        faces.Should().OnlyContain(f => f.GeometryKind == GeometryKind.Polygon);
        faces.Sum(static f => f.Geometry!.Area).Should().BeApproximately(10_000d, 1e-3d,
            "the two triangles reconstitute the square they were split from");
        faces.Should().OnlyContain(f => f.Attributes.ContainsKey("Elevation"));
    }

    [Fact]
    public async Task Breakline_And_Boundary_AreReadFromSourceData()
    {
        SourceDocument document = await ReadAsync();

        SourceElement breakline = document.Layers
            .Single(static layer => layer.Name == "Breaklines:EG").Elements.Single();
        SourceElement boundary = document.Layers
            .Single(static layer => layer.Name == "Boundaries:EG").Elements.Single();

        breakline.GeometryKind.Should().Be(GeometryKind.Line);
        breakline.Attributes.Should().ContainKey("BreaklineType");
        boundary.GeometryKind.Should().Be(GeometryKind.Polygon);
        boundary.Attributes.Should().ContainKey("BoundaryType");
    }

    [Fact]
    public async Task Pipe_PreservesItsConnectivity_AndIsBuiltFromItsStructures()
    {
        SourceDocument document = await ReadAsync();

        SourceElement pipe = document.Layers
            .Single(static layer => layer.Name == "Pipes:STORM").Elements.Single();

        // The pipe declares no geometry of its own, so it is reconstructed end to end.
        pipe.Geometry!.NumPoints.Should().Be(2);
        pipe.Attributes["StartStructure"].Should().Be("MH-1");
        pipe.Attributes["EndStructure"].Should().Be("MH-2");
        pipe.Attributes.Should().ContainKey("Diameter");
        pipe.Attributes.Should().ContainKey("Material");
    }

    [Fact]
    public async Task Structures_AreReadAsPointsWithTheirLevels()
    {
        SourceDocument document = await ReadAsync();

        IReadOnlyList<SourceElement> structures =
            [.. document.Layers.Single(static layer => layer.Name == "Structures:STORM").Elements];

        structures.Should().HaveCount(2);
        structures.Should().OnlyContain(s => s.GeometryKind == GeometryKind.Point);
        structures.Should().OnlyContain(s => s.Attributes.ContainsKey("RimElevation"));
    }

    [Fact]
    public async Task PlanFeature_OpenRun_BecomesALine()
    {
        SourceDocument document = await ReadAsync();

        SourceElement fence = document.Layers
            .Single(static layer => layer.Name == "PlanFeatures:Planimetrics").Elements.Single();

        fence.GeometryKind.Should().Be(GeometryKind.Line, "an open run of coordinates is not an area");
        fence.Attributes["FeatureName"].Should().Be("FENCE-1");
    }

    [Fact]
    public async Task EveryElement_CarriesALayerAttribute_SoRulesCanTargetIt()
    {
        SourceDocument document = await ReadAsync();

        document.Layers.SelectMany(static layer => layer.Elements)
            .Should().OnlyContain(e => e.Attributes.ContainsKey("Layer"),
                "the rule engine and attribute table both key off Layer");
    }

    [Fact]
    public async Task MissingFile_FailsAsAResult_RatherThanThrowing()
    {
        LandXmlReader reader = new(Context());

        Result<SourceDocument> result =
            await reader.ReadAsync(new SourceReference(Path.Combine(Path.GetTempPath(), "no-such-file.landxml")));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("LandXml.FileNotFound");
    }

    [Fact]
    public async Task MalformedXml_FailsAsAResult_RatherThanThrowing()
    {
        string broken = Path.Combine(Path.GetTempPath(), $"aigis-broken-{Guid.NewGuid():N}.landxml");
        File.WriteAllText(broken, "<LandXML><Unclosed>");

        try
        {
            Result<SourceDocument> result = await new LandXmlReader(Context()).ReadAsync(new SourceReference(broken));

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("LandXml.MalformedDocument");
        }
        finally
        {
            File.Delete(broken);
        }
    }

    [Fact]
    public async Task LargeDocument_ReadsWithinAReasonableTime()
    {
        // Ten thousand survey points is a normal topographic pickup; parsing must stay linear.
        string path = Path.Combine(Path.GetTempPath(), $"aigis-large-{Guid.NewGuid():N}.landxml");
        System.Text.StringBuilder builder = new();
        builder.AppendLine("<LandXML xmlns=\"http://www.landxml.org/schema/LandXML-1.2\" version=\"1.2\">");
        builder.AppendLine("<CgPoints name=\"Bulk\">");

        for (int i = 0; i < 10_000; i++)
        {
            builder.AppendLine(
                $"<CgPoint name=\"P{i}\" code=\"TOPO\">{2762000 + i}.000 {483000 + i}.000 {10 + (i % 50)}.000</CgPoint>");
        }

        builder.AppendLine("</CgPoints></LandXML>");
        File.WriteAllText(path, builder.ToString());

        try
        {
            System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
            Result<SourceDocument> result = await new LandXmlReader(Context()).ReadAsync(new SourceReference(path));
            stopwatch.Stop();

            result.IsSuccess.Should().BeTrue();
            result.Value.CountElements().Should().Be(10_000);
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(20),
                "a ten thousand point pickup must not take an operator-visible pause");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
