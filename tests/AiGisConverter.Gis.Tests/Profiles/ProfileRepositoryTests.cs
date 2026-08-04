using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Gis.Options;
using AiGisConverter.Gis.Profiles;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Tests.Profiles;

public sealed class ProfileRepositoryTests
{
    private static ProfileRepository Repository(GisOptions? options = null)
    {
        GisOptions value = options ?? new GisOptions();
        value.ProfileSearchPaths.Clear();

        return new ProfileRepository(Microsoft.Extensions.Options.Options.Create(value), NullLogger<ProfileRepository>.Instance);
    }

    [Theory]
    [InlineData("generic-geojson")]
    [InlineData("esri")]
    [InlineData("qgis")]
    [InlineData("dubai-municipality")]
    public void Get_BuiltInProfile_Resolves(string id) =>
        Repository().Get(id).IsSuccess.Should().BeTrue();

    [Fact]
    public void GetAll_ReturnsTheFourBuiltIns() =>
        Repository().GetAll().Should().HaveCount(4);

    [Fact]
    public void Get_UnknownProfile_NamesTheAlternatives()
    {
        Result<ConversionProfile> result = Repository().Get("does-not-exist");

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("esri", "a format typo is the likeliest cause");
    }

    [Fact]
    public void Get_EsriProfile_InheritsFromGenericGeoJson()
    {
        ConversionProfile esri = Repository().Get("esri").Value;

        esri.ExportFormat.Should().Be(ExportFormat.Shapefile, "the child overrides");
        esri.PrecisionScale.Should().NotBeNull("the parent's value is inherited");
    }

    [Fact]
    public void Get_EsriProfile_ImposesShapefileConstraints()
    {
        ConversionProfile esri = Repository().Get("esri").Value;

        esri.Naming.MaximumFieldNameLength.Should().Be(10, "DBF caps field names at ten characters");
        esri.Geometry.ExteriorRingOrientation.Should().Be(RingOrientationRule.Clockwise);
        esri.Geometry.PromoteToMulti.Should().BeTrue();
    }

    [Fact]
    public void Get_GenericProfile_FollowsRfc7946()
    {
        ConversionProfile generic = Repository().Get("generic-geojson").Value;

        generic.OutputCrs.Should().Be("EPSG:4326");
        generic.Geometry.ExteriorRingOrientation.Should().Be(RingOrientationRule.CounterClockwise);
    }

    [Fact]
    public void Get_DubaiProfile_InheritsTheEsriConstraintsTwoLevelsUp()
    {
        ConversionProfile dubai = Repository().Get("dubai-municipality").Value;

        dubai.ExportFormat.Should().Be(ExportFormat.Shapefile, "inherited from esri");
        dubai.Naming.Prefix.Should().Be("DM_");
        dubai.Qa.FailAtOrAbove.Should().Be(IssueSeverity.Error);
    }

    [Fact]
    public void ResolveFieldName_AppliesMappingThenNamingRules()
    {
        ConversionProfile profile = new()
        {
            Naming = new NamingRules { Case = NameCase.Upper, MaximumFieldNameLength = 10 },
        };

        profile.AttributeMapping["pipe diameter"] = "diameter_millimetres";

        profile.ResolveFieldName("pipe diameter").Should().Be("DIAMETER_M");
    }

    [Fact]
    public void ResolveFieldName_ExcludedAttribute_ReturnsNull()
    {
        ConversionProfile profile = new();
        profile.ExcludedAttributes.Add("HANDLE");

        profile.ResolveFieldName("handle").Should().BeNull();
    }

    [Fact]
    public void NamingRules_CollapseSeparatorRuns()
    {
        NamingRules rules = new() { Separator = "_" };

        rules.Apply("C - STRM - PIPE").Should().Be("C_STRM_PIPE");
    }

    [Fact]
    public void UserProfile_ReplacesTheBuiltInOfTheSameId()
    {
        string directory = Directory.CreateTempSubdirectory("aigis-profiles").FullName;

        try
        {
            File.WriteAllText(
                Path.Combine(directory, "esri.json"),
                """{"id":"esri","name":"Site override","exportFormat":"GeoPackage"}""");

            GisOptions options = new();
            options.ProfileSearchPaths.Clear();
            options.ProfileSearchPaths.Add(directory);

            ProfileRepository repository = new(Microsoft.Extensions.Options.Options.Create(options), NullLogger<ProfileRepository>.Instance);

            repository.Get("esri").Value.ExportFormat.Should().Be(ExportFormat.GeoPackage,
                "a site must be able to correct a shipped profile without waiting for a release");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
