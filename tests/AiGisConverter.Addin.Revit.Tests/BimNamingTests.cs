using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;

namespace AiGisConverter.Addin.Revit.Tests
{
    /// <summary>
    /// Tests for the attribute key and value rules applied to exported BIM parameters.
    /// </summary>
    /// <remarks>
    /// These rules decide what every consumer of the export sees. The Revit-bound half of the
    /// semantic reader cannot be exercised without Revit; this half can, and it is the half where a
    /// mistake is silent - a mangled key, a decimal comma, a user parameter quietly displacing
    /// <c>Category</c>.
    /// </remarks>
    public sealed class BimNamingTests
    {
        // ---------------------------------------------------------------- keys

        [Theory]
        [InlineData("Base Constraint", "Base_Constraint")]
        [InlineData("Top Offset", "Top_Offset")]
        [InlineData("IfcGUID", "IfcGUID")]
        [InlineData("Fire Rating (hr)", "Fire_Rating_hr")]
        [InlineData("Cost/Unit", "Cost_Unit")]
        [InlineData("  padded  ", "padded")]
        [InlineData("Already_Fine", "Already_Fine")]
        public void SanitiseProducesASafeKey(string name, string expected)
        {
            BimNaming.Sanitise(name).Should().Be(expected);
        }

        [Fact]
        public void SanitiseNeverEmitsRepeatedOrTrailingSeparators()
        {
            BimNaming.Sanitise("A -- B").Should().Be("A_B");
            BimNaming.Sanitise("trailing---").Should().Be("trailing");
            BimNaming.Sanitise("---leading").Should().Be("leading");
        }

        [Fact]
        public void SanitiseKeepsNonLatinNames()
        {
            // The model driving this work is authored in Chinese. Reducing its parameter names to
            // ASCII would replace every one of them with a row of underscores: valid keys naming
            // nothing, and unrecoverable once exported.
            BimNaming.Sanitise("面积").Should().Be("面积");
            BimNaming.Sanitise("防火 等级").Should().Be("防火_等级");
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("---")]
        [InlineData("!@#$%")]
        public void SanitiseRejectsANameWithNothingUsableInIt(string? name)
        {
            BimNaming.Sanitise(name).Should().BeNull();
        }

        [Fact]
        public void ParameterKeysCannotCollideWithReaderFields()
        {
            // A model is free to contain a user parameter called "Category". The prefix is what
            // stops it displacing the reader's own Category, and it does so by construction rather
            // than by a list of reserved words that would need extending with every new field.
            BimNaming.InstanceKey("Category").Should().Be("p_Category");
            BimNaming.TypeKey("Category").Should().Be("tp_Category");

            BimNaming.InstanceKey("Category").Should().NotBe("Category");
            BimNaming.TypeKey("Category").Should().NotBe("Category");
        }

        [Fact]
        public void InstanceAndTypeParametersOfTheSameNameStayDistinct()
        {
            // Both exist on most elements and routinely differ. Merging them would silently pick one.
            BimNaming.InstanceKey("Comments").Should().NotBe(BimNaming.TypeKey("Comments"));
        }

        [Fact]
        public void KeysRejectAnUnusableName()
        {
            BimNaming.InstanceKey("###").Should().BeNull();
            BimNaming.TypeKey(null).Should().BeNull();
        }

        // ---------------------------------------------------------------- values

        [Fact]
        public void NumbersAreWrittenInvariantly()
        {
            CultureInfo original = Thread.CurrentThread.CurrentCulture;

            try
            {
                // A locale whose decimal separator is a comma. Written that way, 3.5 metres is read
                // on the far side as 35, and nothing downstream can tell.
                Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");

                BimNaming.Number(3.5d).Should().Be("3.5");
                BimNaming.Number(-0.125d).Should().Be("-0.125");
                BimNaming.Integer(1234).Should().Be("1234");
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = original;
            }
        }

        [Fact]
        public void NumbersRoundTrip()
        {
            double value = 1d / 3d;

            double.Parse(BimNaming.Number(value), CultureInfo.InvariantCulture).Should().Be(value);
        }

        [Theory]
        [InlineData(double.NaN)]
        [InlineData(double.PositiveInfinity)]
        [InlineData(double.NegativeInfinity)]
        public void NonFiniteNumbersAreSkippedRatherThanWritten(double value)
        {
            // "NaN" in a column every consumer has inferred as numeric is worse than an absent value.
            BimNaming.Number(value).Should().BeNull();
        }

        [Fact]
        public void YesNoIsWrittenAsABoolean()
        {
            BimNaming.YesNo(1).Should().Be("true");
            BimNaming.YesNo(0).Should().Be("false");
            BimNaming.YesNo(-1).Should().Be("true");
        }

        // ---------------------------------------------------------------- bounds

        [Fact]
        public void ShortValuesPassThroughUntouched()
        {
            BimNaming.Truncate("a short value").Should().Be("a short value");
            BimNaming.Truncate(null).Should().BeNull();
            BimNaming.Truncate(string.Empty).Should().BeEmpty();
        }

        [Fact]
        public void LongValuesAreBoundedAndMarked()
        {
            string oversized = new string('x', BimNaming.MaximumValueLength * 3);
            string result = BimNaming.Truncate(oversized);

            result.Length.Should().Be(BimNaming.MaximumValueLength);
            result.Should().EndWith(BimNaming.TruncationMarker);
        }

        [Fact]
        public void AValueExactlyAtTheLimitIsNotMarked()
        {
            string exact = new string('x', BimNaming.MaximumValueLength);

            BimNaming.Truncate(exact).Should().Be(exact);
        }

        // ---------------------------------------------------------------- joining

        [Fact]
        public void MultipleMaterialsJoinIntoOneValue()
        {
            BimNaming.Join(new List<string> { "Concrete", "Rebar", "Paint" })
                .Should().Be("Concrete; Rebar; Paint");
        }

        [Fact]
        public void JoiningSkipsBlanksAndYieldsNullWhenNothingRemains()
        {
            BimNaming.Join(new List<string> { "Concrete", null, string.Empty, "Rebar" })
                .Should().Be("Concrete; Rebar");

            BimNaming.Join(new List<string>()).Should().BeNull();
            BimNaming.Join(new List<string> { null, string.Empty }).Should().BeNull();
            BimNaming.Join(null).Should().BeNull();
        }

        [Fact]
        public void JoiningUsesASeparatorThatSurvivesRevitNames()
        {
            // Revit names contain commas often enough that a comma-joined value could not be split
            // back apart. A semicolon can.
            BimNaming.Join(new List<string> { "Concrete, Cast-in-Place", "Steel, Carbon" })
                .Should().Be("Concrete, Cast-in-Place; Steel, Carbon");
        }
    }
}
