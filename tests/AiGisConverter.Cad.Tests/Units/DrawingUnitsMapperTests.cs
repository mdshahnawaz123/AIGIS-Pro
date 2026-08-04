using AiGisConverter.Cad.Units;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Cad.Tests.Units;

public sealed class DrawingUnitsMapperTests
{
    [Theory]
    [InlineData(1, LinearUnit.Inch)]
    [InlineData(2, LinearUnit.Foot)]
    [InlineData(4, LinearUnit.Millimetre)]
    [InlineData(5, LinearUnit.Centimetre)]
    [InlineData(6, LinearUnit.Metre)]
    [InlineData(7, LinearUnit.Kilometre)]
    [InlineData(21, LinearUnit.UsSurveyFoot)]
    public void FromInsUnits_MapsTheDocumentedCodes(int code, LinearUnit expected) =>
        DrawingUnitsMapper.FromInsUnits(code).Should().Be(expected);

    [Fact]
    public void FromInsUnits_DistinguishesSurveyFeetFromInternationalFeet() =>
        DrawingUnitsMapper.FromInsUnits(21).Should().NotBe(DrawingUnitsMapper.FromInsUnits(2),
            "the two differ by two parts per million, which is about a metre across a state plane zone");

    [Theory]
    [InlineData(0)]   // unitless
    [InlineData(14)]  // decimetres
    [InlineData(20)]  // parsecs
    [InlineData(999)]
    public void FromInsUnits_UnsupportedCode_IsUnknownRatherThanAGuess(int code) =>
        DrawingUnitsMapper.FromInsUnits(code).Should().Be(LinearUnit.Unknown,
            "guessing here is an order-of-magnitude scale error nothing downstream could detect");

    [Fact]
    public void DisplayName_UnknownUnit_ReadsAsUnspecified() =>
        DrawingUnitsMapper.DisplayName(LinearUnit.Unknown).Should().Be("unspecified");
}
