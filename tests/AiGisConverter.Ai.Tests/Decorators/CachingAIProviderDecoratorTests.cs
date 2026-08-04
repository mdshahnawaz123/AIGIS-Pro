using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Caching;
using AiGisConverter.Ai.Decorators;
using AiGisConverter.Ai.Features;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Ai.Tests.Decorators;

public sealed class CachingAIProviderDecoratorTests
{
    private static IAIProvider Wrap(FakeAiProvider inner, bool caching = true)
    {
        CachingAIProviderDecorator decorator = new(
            new InMemoryAIResponseCache(TimeProvider.System),
            new AIRequestCacheKeyFactory(new SubjectDescriptor()),
            AiTestFactory.Options(o => o.EnableCaching = caching),
            NullLoggerFactory.Instance);

        return decorator.Decorate(inner);
    }

    [Fact]
    public async Task ClassifyAsync_SecondIdenticalRequest_IsServedFromTheCache()
    {
        FakeAiProvider inner = new();
        IAIProvider provider = Wrap(inner);

        await provider.ClassifyAsync(AiTestFactory.Request());
        await provider.ClassifyAsync(AiTestFactory.Request());

        inner.ClassifyCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ClassifyAsync_DifferentSubjects_MissTheCache()
    {
        FakeAiProvider inner = new();
        IAIProvider provider = Wrap(inner);

        await provider.ClassifyAsync(AiTestFactory.Request([AiTestFactory.Subject("L1", "C-WTR")]));
        await provider.ClassifyAsync(AiTestFactory.Request([AiTestFactory.Subject("L1", "C-STRM")]));

        inner.ClassifyCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ClassifyAsync_CachingDisabled_AlwaysCallsThrough()
    {
        FakeAiProvider inner = new();
        IAIProvider provider = Wrap(inner, caching: false);

        await provider.ClassifyAsync(AiTestFactory.Request());
        await provider.ClassifyAsync(AiTestFactory.Request());

        inner.ClassifyCallCount.Should().Be(2);
    }

    [Fact]
    public async Task ClassifyAsync_CachedResults_AreIndependentBetweenCallers()
    {
        FakeAiProvider inner = new();
        IAIProvider provider = Wrap(inner);

        AIClassificationResponse first = await provider.ClassifyAsync(AiTestFactory.Request());
        first.Results[0].MarkAccepted(true);

        AIClassificationResponse second = await provider.ClassifyAsync(AiTestFactory.Request());

        second.Results[0].IsAccepted.Should().BeFalse("one caller's acceptance must not reach another's");
        second.Results[0].Should().NotBeSameAs(first.Results[0]);
    }
}
