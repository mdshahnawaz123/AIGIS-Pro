using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Cad.Providers.AutoCad;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Cad.Tests.Providers;

public sealed class AutoCadProviderTests
{
    private static AutoCadProvider CreateProvider(IDwgBackend? backend = null) =>
        new(backend ?? new UnavailableDwgBackend(), NullLogger<AutoCadProvider>.Instance);

    [Fact]
    public void Provider_ClaimsDwgSoTheUserGetsAnExplanationRatherThanSilence()
    {
        CreateProvider().SupportedExtensions.Should().Contain(".dwg");
    }

    [Fact]
    public async Task ProbeAsync_WithoutAnEngine_ReportsUnavailableWithGuidance()
    {
        CadProviderAvailability availability = await CreateProvider().ProbeAsync();

        availability.IsAvailable.Should().BeFalse();
        availability.Reason.Should().Contain("DXF", "the message has to tell the user what to do instead");
    }

    [Fact]
    public async Task ReadAsync_WithoutAnEngine_FailsWithAnActionableError()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.dwg");
        File.WriteAllText(path, "not really a dwg");

        try
        {
            Result<SourceDocument> result = await CreateProvider().ReadAsync(new SourceReference(path));

            result.IsFailure.Should().BeTrue();
            result.Error.Code.Should().Be("Cad.DwgEngineUnavailable");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ReadAsync_WithARegisteredEngine_DelegatesToIt()
    {
        SourceReference reference = new(Path.Combine(Path.GetTempPath(), "delegated.dwg"));
        SourceDocument expected = new(reference, "dwg");

        IDwgBackend backend = Substitute.For<IDwgBackend>();
        backend.ProbeAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CadProviderAvailability.Available("test engine")));
        backend.ReadAsync(
                Arg.Any<SourceReference>(),
                Arg.Any<IProgress<ReadProgress>?>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Result.Success(expected)));

        Result<SourceDocument> result = await CreateProvider(backend).ReadAsync(reference);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(expected);
    }
}
