using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Cad.Factories;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Cad.Tests.Factories;

public sealed class CadProviderFactoryTests
{
    private static ICadProvider Provider(string key, string extension, bool claims)
    {
        ICadProvider provider = Substitute.For<ICadProvider>();
        provider.Key.Returns(key);
        provider.SupportedExtensions.Returns(new[] { extension });
        provider.CanRead(Arg.Any<SourceReference>()).Returns(claims);

        return provider;
    }

    [Fact]
    public void Resolve_ReturnsTheProviderThatClaimsTheFile()
    {
        ICadProvider dxf = Provider("dxf", ".dxf", claims: false);
        ICadProvider dwg = Provider("dwg", ".dwg", claims: true);

        CadProviderFactory factory = new([dxf, dwg]);

        factory.Resolve(new SourceReference(@"C:\site.dwg")).Should().BeSameAs(dwg);
    }

    [Fact]
    public void Resolve_WhenNothingClaims_ReturnsNull()
    {
        CadProviderFactory factory = new([Provider("dxf", ".dxf", claims: false)]);

        factory.Resolve(new SourceReference(@"C:\site.ifc")).Should().BeNull();
    }

    [Fact]
    public void Resolve_RegistrationOrderBreaksTies()
    {
        // A licensed engine registered ahead of the stub wins, without either knowing the other exists.
        ICadProvider licensed = Provider("dwg-licensed", ".dwg", claims: true);
        ICadProvider stub = Provider("dwg", ".dwg", claims: true);

        CadProviderFactory factory = new([licensed, stub]);

        factory.Resolve(new SourceReference(@"C:\site.dwg")).Should().BeSameAs(licensed);
    }

    [Theory]
    [InlineData("DXF")]
    [InlineData("dxf")]
    public void ResolveByKey_IsCaseInsensitive(string key)
    {
        CadProviderFactory factory = new([Provider("dxf", ".dxf", claims: false)]);

        factory.ResolveByKey(key).Should().NotBeNull();
    }

    [Fact]
    public void ResolveByKey_BlankKey_ReturnsNull() =>
        new CadProviderFactory([Provider("dxf", ".dxf", false)]).ResolveByKey("  ").Should().BeNull();
}
