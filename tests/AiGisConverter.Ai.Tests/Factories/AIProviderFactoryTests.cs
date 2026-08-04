using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Factories;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Options;
using AiGisConverter.Ai.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Ai.Tests.Factories;

public sealed class AIProviderFactoryTests
{
    private static AIProviderFactory Create(
        IEnumerable<IAIProviderSource> sources,
        Action<AiOptions>? configure = null) =>
        new(sources, AiTestFactory.Options(configure), NullLogger<AIProviderFactory>.Instance);

    private static IAIProviderSource Source(params IAIProvider[] providers) =>
        new ServiceProviderAIProviderSource(providers);

    [Fact]
    public void GetProvider_ByKey_IsCaseInsensitive() =>
        Create([Source(new FakeAiProvider("ollama"))]).GetProvider("OLLAMA").Key.Should().Be("ollama");

    [Fact]
    public void GetProvider_UnknownKey_NamesWhatIsRegistered()
    {
        Action act = () => Create([Source(new FakeAiProvider("ollama"), new FakeAiProvider("onnx"))])
            .GetProvider("openai");

        act.Should().Throw<AIProviderNotRegisteredException>()
            .WithMessage("*ollama*").WithMessage("*onnx*");
    }

    [Fact]
    public void GetActiveProvider_NoKeyConfigured_PicksTheLeastDemanding()
    {
        IAIProvider network = Substitute.For<IAIProvider>();
        network.Key.Returns("openai");
        network.Metadata.Returns(new AIProviderMetadata(
            "openai", "OpenAI", AIProviderKind.RemoteService, 25, true, RequiresNetwork: true));

        AIProviderFactory factory = Create([Source(network, new FakeAiProvider("rulebased"))]);

        factory.GetActiveProvider().Key.Should().Be("rulebased",
            "an offline provider is preferred when configuration names none");
    }

    [Fact]
    public void GetActiveProvider_NoProvidersAtAll_Throws()
    {
        Action act = () => Create([Source()]).GetActiveProvider();

        act.Should().Throw<AIProviderNotRegisteredException>();
    }

    [Fact]
    public void GetFallbackProvider_Disabled_ReturnsNull() =>
        Create([Source(new FakeAiProvider("a"))], o => o.FallbackProvider = AiOptions.DisabledFallback)
            .GetFallbackProvider().Should().BeNull();

    [Fact]
    public void GetFallbackProvider_UnknownKey_ReturnsNullRatherThanThrowing() =>
        Create([Source(new FakeAiProvider("a"))], o => o.FallbackProvider = "ghost")
            .GetFallbackProvider().Should().BeNull("a missing fallback degrades, it does not break");

    [Fact]
    public void GetRegisteredProviders_ListsEverySourceOrderedByKey()
    {
        AIProviderFactory factory = Create(
            [Source(new FakeAiProvider("zeta")), Source(new FakeAiProvider("alpha"))]);

        factory.GetRegisteredProviders().Select(m => m.Key).Should().ContainInOrder("alpha", "zeta");
    }

    [Fact]
    public void DuplicateKeys_KeepTheFirstAndDoNotThrow()
    {
        // A badly named third-party plugin must not disable AI classification.
        FakeAiProvider first = new("dup");

        AIProviderFactory factory = Create([Source(first), Source(new FakeAiProvider("dup"))]);

        factory.GetProvider("dup").Should().BeSameAs(first);
    }

    [Fact]
    public void Refresh_PicksUpProvidersRegisteredAfterConstruction()
    {
        MutableSource source = new();
        AIProviderFactory factory = Create([source]);

        factory.GetRegisteredProviders().Should().BeEmpty();

        source.Providers.Add(new FakeAiProvider("late"));
        factory.Refresh();

        factory.GetRegisteredProviders().Should().ContainSingle(
            "plugin providers do not exist when the container is built");
    }

    [Fact]
    public void Index_IsCachedUntilRefreshed()
    {
        MutableSource source = new();
        source.Providers.Add(new FakeAiProvider("a"));

        AIProviderFactory factory = Create([source]);
        factory.GetRegisteredProviders();

        source.Providers.Add(new FakeAiProvider("b"));

        factory.GetRegisteredProviders().Should().ContainSingle("the index is not rebuilt on every call");
    }

    private sealed class MutableSource : IAIProviderSource
    {
        public List<IAIProvider> Providers { get; } = [];

        public IEnumerable<IAIProvider> GetProviders() => Providers;
    }
}
