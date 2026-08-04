using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Features;
using AiGisConverter.Gis.Profiles;

namespace AiGisConverter.Gis.Tests.Features;

public sealed class AttributeMapperTests
{
    private static SourceLayer LayerWith(params (string Key, object? Value)[] attributes)
    {
        SourceLayer layer = new("PARCELS");
        SourceElement element = new("1", GeometryKind.Polygon);

        foreach ((string key, object? value) in attributes)
        {
            element.SetAttribute(key, value);
        }

        layer.AddElement(element);

        return layer;
    }

    private static SourceLayer LayerWithValues(string field, params object?[] values)
    {
        SourceLayer layer = new("PARCELS");
        int id = 0;

        foreach (object? value in values)
        {
            SourceElement element = new((++id).ToString(), GeometryKind.Polygon);
            element.SetAttribute(field, value);
            layer.AddElement(element);
        }

        return layer;
    }

    [Fact]
    public void BuildSchema_LeadingZeros_ForceText()
    {
        // A plot reference of 00742 read as the integer 742 has lost information that cannot be
        // recovered, and nobody notices until a registry cross-reference fails.
        GisAttributeSchema schema = new AttributeMapper()
            .BuildSchema(LayerWithValues("PLOT", "00742", "00815"), new ConversionProfile());

        schema.Find("PLOT")!.DataType.Should().Be(AttributeDataType.Text);
    }

    [Fact]
    public void BuildSchema_PlainIntegers_BecomeLong()
    {
        GisAttributeSchema schema = new AttributeMapper()
            .BuildSchema(LayerWithValues("COUNT", "12", "3400"), new ConversionProfile());

        schema.Find("COUNT")!.DataType.Should().Be(AttributeDataType.Long);
    }

    [Fact]
    public void BuildSchema_OneNonNumericValue_MakesTheWholeColumnText()
    {
        GisAttributeSchema schema = new AttributeMapper()
            .BuildSchema(LayerWithValues("TAG", "1", "2", "3", "N/A"), new ConversionProfile());

        schema.Find("TAG")!.DataType.Should().Be(AttributeDataType.Text,
            "a column that is numeric in one drawing and text in the next cannot be appended");
    }

    [Fact]
    public void BuildSchema_DecimalsBecomeDouble()
    {
        GisAttributeSchema schema = new AttributeMapper()
            .BuildSchema(LayerWithValues("DIA", "0.15", "0.225"), new ConversionProfile());

        schema.Find("DIA")!.DataType.Should().Be(AttributeDataType.Double,
            "0.15 has a leading zero but the next character is a decimal point");
    }

    [Fact]
    public void BuildSchema_HonoursExcludedAttributes()
    {
        ConversionProfile profile = new();
        profile.ExcludedAttributes.Add("HANDLE");

        GisAttributeSchema schema = new AttributeMapper()
            .BuildSchema(LayerWith(("HANDLE", "2AF"), ("PLOT", "1")), profile);

        schema.Contains("HANDLE").Should().BeFalse();
        schema.Contains("PLOT").Should().BeTrue();
    }

    [Fact]
    public void BuildSchema_AppliesAttributeMappingAndNaming()
    {
        ConversionProfile profile = new()
        {
            Naming = new NamingRules { Case = NameCase.Upper, MaximumFieldNameLength = 10 },
        };

        profile.AttributeMapping["diameter_millimetres"] = "pipe_diameter_mm";

        GisAttributeSchema schema = new AttributeMapper()
            .BuildSchema(LayerWith(("diameter_millimetres", "150")), profile);

        schema.Fields.Should().ContainSingle();
        schema.Fields[0].Name.Should().Be("PIPE_DIAME", "Shapefile caps DBF field names at ten characters");
    }

    [Fact]
    public void Map_MissingAttribute_BecomesTypedNullNotAbsent()
    {
        AttributeMapper mapper = new();
        ConversionProfile profile = new();

        GisAttributeSchema schema = mapper.BuildSchema(
            LayerWithValues("PLOT", "1", "2"), profile);

        SourceElement bare = new("99", GeometryKind.Polygon);

        IReadOnlyDictionary<string, AttributeValue> mapped = mapper.Map(bare, schema, profile);

        mapped.Should().ContainKey("PLOT");
        mapped["PLOT"].IsNull.Should().BeTrue();
    }

    [Fact]
    public void Map_ValueContradictingTheSchema_IsKeptAsText()
    {
        AttributeMapper mapper = new();
        ConversionProfile profile = new();

        GisAttributeSchema schema = mapper.BuildSchema(LayerWithValues("N", "1", "2"), profile);

        SourceElement odd = new("3", GeometryKind.Polygon);
        odd.SetAttribute("N", "not-a-number");

        // Forcing the declared type would replace a real value with a null.
        mapper.Map(odd, schema, profile)["N"].IsNull.Should().BeFalse();
    }
}
