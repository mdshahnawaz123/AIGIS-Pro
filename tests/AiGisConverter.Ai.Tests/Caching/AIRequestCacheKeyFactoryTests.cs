using AiGisConverter.Ai.Caching;
using AiGisConverter.Ai.Features;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Tests.TestSupport;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Tests.Caching;

public sealed class AIRequestCacheKeyFactoryTests
{
    private static readonly AIRequestCacheKeyFactory Factory = new(new SubjectDescriptor());

    [Fact]
    public void Create_IsStableForTheSameRequest()
    {
        AIClassificationRequest request = AiTestFactory.Request();

        Factory.Create("ollama", request).Should().Be(Factory.Create("ollama", request));
    }

    [Fact]
    public void Create_IsStableAcrossEquivalentRequests()
    {
        // Determinism across instances is what makes the key survive a restart.
        Factory.Create("ollama", AiTestFactory.Request())
            .Should().Be(Factory.Create("ollama", AiTestFactory.Request()));
    }

    [Fact]
    public void Create_DiffersByProvider() =>
        Factory.Create("ollama", AiTestFactory.Request())
            .Should().NotBe(Factory.Create("openai", AiTestFactory.Request()));

    [Fact]
    public void Create_DiffersWhenASubjectChanges()
    {
        AIClassificationRequest first = AiTestFactory.Request([AiTestFactory.Subject("L1", "C-WTR-MAIN")]);
        AIClassificationRequest second = AiTestFactory.Request([AiTestFactory.Subject("L1", "C-STRM-PIPE")]);

        Factory.Create("ollama", first).Should().NotBe(Factory.Create("ollama", second));
    }

    [Fact]
    public void Create_DiffersWhenTheLabelSetChanges()
    {
        AIClassificationRequest first = new([AiTestFactory.Subject()], AiTestFactory.Context("A", "B"));
        AIClassificationRequest second = new([AiTestFactory.Subject()], AiTestFactory.Context("A", "C"));

        Factory.Create("ollama", first).Should().NotBe(Factory.Create("ollama", second));
    }

    [Fact]
    public void Create_IsPrefixedByTheProviderKey() =>
        Factory.Create("ollama", AiTestFactory.Request()).Should().StartWith("ollama:");
}
