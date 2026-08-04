using AiGisConverter.Ai.Caching;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Tests.TestSupport;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Tests.Caching;

public sealed class InMemoryAIResponseCacheTests
{
    private static readonly TimeSpan OneHour = TimeSpan.FromHours(1);

    private static (InMemoryAIResponseCache Cache, FakeTimeProvider Clock) Create()
    {
        FakeTimeProvider clock = new();

        return (new InMemoryAIResponseCache(clock), clock);
    }

    [Fact]
    public void TryGet_Miss_ReturnsFalse()
    {
        (InMemoryAIResponseCache cache, _) = Create();

        cache.TryGet("absent", out AIClassificationResponse? response).Should().BeFalse();
        response.Should().BeNull();
    }

    [Fact]
    public void TryGet_AfterSet_ReturnsTheStoredValue()
    {
        (InMemoryAIResponseCache cache, _) = Create();
        cache.Set("k", AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d)), OneHour);

        cache.TryGet("k", out AIClassificationResponse? response).Should().BeTrue();
        response!.Results.Should().ContainSingle();
        response.Results[0].SubjectId.Should().Be("L1");
    }

    [Fact]
    public void TryGet_AfterExpiry_ReturnsFalse()
    {
        (InMemoryAIResponseCache cache, FakeTimeProvider clock) = Create();
        cache.Set("k", AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d)), TimeSpan.FromMinutes(5));

        clock.Advance(TimeSpan.FromMinutes(6));

        cache.TryGet("k", out _).Should().BeFalse();
    }

    [Fact]
    public void Clear_EmptiesTheCache()
    {
        (InMemoryAIResponseCache cache, _) = Create();
        cache.Set("k", AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d)), OneHour);

        cache.Clear();

        cache.TryGet("k", out _).Should().BeFalse();
    }

    [Fact]
    public void TryGet_DoesNotHandBackTheStoredInstance()
    {
        // The defect this pins: a cache that returns its own instance is a pool of shared
        // mutable objects, and ClassificationResult carries an acceptance flag each caller stamps.
        (InMemoryAIResponseCache cache, _) = Create();
        cache.Set("k", AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d)), OneHour);

        cache.TryGet("k", out AIClassificationResponse? first).Should().BeTrue();
        cache.TryGet("k", out AIClassificationResponse? second).Should().BeTrue();

        first!.Results[0].Should().NotBeSameAs(second!.Results[0]);
    }

    [Fact]
    public void MutatingAReadResult_DoesNotAffectTheNextRead()
    {
        (InMemoryAIResponseCache cache, _) = Create();
        cache.Set("k", AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d)), OneHour);

        cache.TryGet("k", out AIClassificationResponse? first);
        first!.Results[0].MarkAccepted(true);
        first.Results[0].AddAlternative(new ClassificationCandidate("Injected", Domain.ValueObjects.Confidence.Certain));

        cache.TryGet("k", out AIClassificationResponse? second);

        second!.Results[0].IsAccepted.Should().BeFalse();
        second.Results[0].Alternatives.Should().HaveCount(1, "the injected alternative must not reach the cache");
    }

    [Fact]
    public void MutatingTheStoredInstanceAfterSet_DoesNotAffectTheCache()
    {
        (InMemoryAIResponseCache cache, _) = Create();
        AIClassificationResponse original = AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d));

        cache.Set("k", original, OneHour);
        original.Results[0].MarkAccepted(true);

        cache.TryGet("k", out AIClassificationResponse? stored);

        stored!.Results[0].IsAccepted.Should().BeFalse("the caller keeps its own instance after Set");
    }

    [Fact]
    public void Clone_PreservesEveryField()
    {
        ClassificationResult original = AiTestFactory.Result("L1", 0.77d, "Water Main", "ollama");
        original.MarkAccepted(true);

        ClassificationResult copy = ClassificationResponseCloner.Clone(original);

        copy.SubjectId.Should().Be(original.SubjectId);
        copy.Label.Should().Be(original.Label);
        copy.Confidence.Value.Should().Be(original.Confidence.Value);
        copy.ProviderKey.Should().Be(original.ProviderKey);
        copy.Rationale.Should().Be(original.Rationale);
        copy.IsAccepted.Should().BeTrue();
        copy.Alternatives.Should().HaveCount(original.Alternatives.Count);
    }

    /// <summary>A clock the test drives, so expiry does not require waiting.</summary>
    private sealed class FakeTimeProvider : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now = _now.Add(by);
    }
}
