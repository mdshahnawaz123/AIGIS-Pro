using System.Reflection;

namespace AiGisConverter.IntegrationTests;

/// <summary>
/// Architecture rules, asserted against the compiled assemblies.
/// </summary>
/// <remarks>
/// These are the checks that stop the design eroding. A layering rule that lives only in a
/// document is a rule that will be broken by someone in a hurry; asserted here it fails the build
/// instead.
/// </remarks>
public sealed class ArchitectureTests
{
    private static Assembly Domain => typeof(Domain.Common.Result).Assembly;
    private static Assembly Application => typeof(Application.Pipelines.ConversionPipeline).Assembly;
    private static Assembly Gis => typeof(Gis.Geometry.GeometryValidator).Assembly;
    private static Assembly Cad => typeof(Cad.Geometry.CurveTessellator).Assembly;
    private static Assembly QaQc => typeof(QaQc.Engine.QaQcEngine).Assembly;
    private static Assembly Ai => typeof(Ai.Factories.AIProviderFactory).Assembly;

    private static IReadOnlyList<string> ReferencedNames(Assembly assembly) =>
        [.. assembly.GetReferencedAssemblies().Select(static a => a.Name ?? string.Empty)];

    [Fact]
    public void Domain_ReferencesNoOtherLayer() =>
        ReferencedNames(Domain).Should().NotContain(name => name.StartsWith("AiGisConverter.", StringComparison.Ordinal),
            "the domain is the centre; everything points inward at it");

    [Fact]
    public void Domain_ReferencesNoCadOrGisEngine()
    {
        IReadOnlyList<string> refs = ReferencedNames(Domain);

        refs.Should().NotContain("netDxf.netstandard");
        refs.Should().NotContain(name => name.StartsWith("OSGeo", StringComparison.Ordinal));
        refs.Should().NotContain(name => name.StartsWith("Autodesk", StringComparison.Ordinal));
    }

    [Fact]
    public void Application_ReferencesOnlyTheDomain() =>
        ReferencedNames(Application)
            .Where(static name => name.StartsWith("AiGisConverter.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(["AiGisConverter.Domain"],
                "the application layer coordinates modules through ports, never by naming them");

    [Theory]
    [InlineData("AiGisConverter.Gis")]
    [InlineData("AiGisConverter.Cad")]
    [InlineData("AiGisConverter.Ai")]
    [InlineData("AiGisConverter.QaQc")]
    [InlineData("AiGisConverter.Data")]
    public void NoModule_ReferencesAnotherModule(string assemblyName)
    {
        Assembly assembly = assemblyName switch
        {
            "AiGisConverter.Gis" => Gis,
            "AiGisConverter.Cad" => Cad,
            "AiGisConverter.Ai" => Ai,
            "AiGisConverter.QaQc" => QaQc,
            _ => typeof(Data.Context.AiGisConverterDbContext).Assembly,
        };

        ReferencedNames(assembly)
            .Where(static name => name.StartsWith("AiGisConverter.", StringComparison.Ordinal))
            .Should().BeEquivalentTo(["AiGisConverter.Domain"]);
    }

    [Fact]
    public void Gis_DoesNotReferenceAnyCadEngine() =>
        ReferencedNames(Gis).Should().NotContain("netDxf.netstandard",
            "the GIS engine consumes domain models, never a drawing format");

    [Fact]
    public void QaQc_DoesNotReferenceTheGisEngine() =>
        ReferencedNames(QaQc).Should().NotContain("AiGisConverter.Gis",
            "quality rules run over domain datasets so a plugin can contribute one");

    [Fact]
    public void EveryDomainPort_IsAnInterface()
    {
        IEnumerable<Type> ports = Domain.GetTypes()
            .Where(static t => t.Namespace?.Contains("Abstractions", StringComparison.Ordinal) == true)
            .Where(static t => t.IsPublic);

        ports.Should().OnlyContain(t => t.IsInterface || t.IsSealed || t.IsValueType || t.IsEnum,
            "an abstraction that is an open class invites inheritance the domain cannot govern");
    }
}
