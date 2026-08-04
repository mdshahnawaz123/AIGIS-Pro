using AiGisConverter.Cad.Crs;

namespace AiGisConverter.Cad.Tests.Crs;

public sealed class SidecarCrsReaderTests
{
    private const string BritishNationalGridWkt =
        """
        PROJCS["OSGB36 / British National Grid",
          GEOGCS["OSGB36",
            DATUM["OSGB_1936", SPHEROID["Airy 1830",6377563.396,299.3249646, AUTHORITY["EPSG","7001"]],
              AUTHORITY["EPSG","6277"]],
            PRIMEM["Greenwich",0, AUTHORITY["EPSG","8901"]],
            UNIT["degree",0.0174532925199433, AUTHORITY["EPSG","9122"]],
            AUTHORITY["EPSG","4277"]],
          UNIT["metre",1, AUTHORITY["EPSG","9001"]],
          AUTHORITY["EPSG","27700"]]
        """;

    [Fact]
    public void ExtractAuthorityCode_TakesTheOutermostAuthority()
    {
        // The first AUTHORITY in this string is the Airy 1830 spheroid. Taking it - a common
        // shortcut - would report a British National Grid drawing as EPSG:7001.
        SidecarCrsReader.ExtractAuthorityCode(BritishNationalGridWkt).Should().Be("EPSG:27700");
    }

    [Fact]
    public void ExtractAuthorityCode_AcceptsTheIdFormOfNewerWkt() =>
        SidecarCrsReader.ExtractAuthorityCode("""PROJCRS["ETRS89 / UTM zone 32N", ID["EPSG",25832]]""")
            .Should().Be("EPSG:25832");

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("LOCAL_CS[\"unnamed\"]")]
    public void ExtractAuthorityCode_WithoutAnAuthority_ReturnsNull(string wkt) =>
        SidecarCrsReader.ExtractAuthorityCode(wkt).Should().BeNull();

    [Fact]
    public void TryRead_FindsASidecarBesideTheDrawing()
    {
        string directory = Directory.CreateTempSubdirectory("aigis-cad-tests").FullName;

        try
        {
            string drawing = Path.Combine(directory, "site-survey.dxf");
            File.WriteAllText(drawing, "placeholder");
            File.WriteAllText(Path.Combine(directory, "site-survey.prj"), BritishNationalGridWkt);

            SidecarCrsReader.TryRead(drawing, out SidecarCrs? crs).Should().BeTrue();

            crs!.AuthorityCode.Should().Be("EPSG:27700");
            crs.WellKnownText.Should().Contain("British National Grid");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRead_WithoutASidecar_ReturnsFalse()
    {
        string directory = Directory.CreateTempSubdirectory("aigis-cad-tests").FullName;

        try
        {
            string drawing = Path.Combine(directory, "no-sidecar.dxf");
            File.WriteAllText(drawing, "placeholder");

            SidecarCrsReader.TryRead(drawing, out _).Should().BeFalse();
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void TryRead_BlankPath_ReturnsFalse() =>
        SidecarCrsReader.TryRead("   ", out _).Should().BeFalse();
}
