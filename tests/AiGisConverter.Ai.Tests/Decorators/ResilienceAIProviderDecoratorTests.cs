using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Decorators;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Ai.Tests.Decorators;

public sealed class ResilienceAIProviderDecoratorTests
{
    private static IAIProvider Wrap(FakeAiProvider inner, Action<AiGisConverter.Ai.Options.AiOptions>? configure = null)
    {
        ResilienceAIProviderDecorator decorator = new(
            AiTestFactory.Options(configure ?? (o =>
            {
                o.Resilience.MaxRetries = 2;
                o.Resilience.BaseDelayMilliseconds = 1;
                o.Resilience.PerAttemptTimeoutSeconds = 30;
            })),
            NullLoggerFactory.Instance);

        return decorator.Decorate(inner);
    }

    [Fact]
    public async Task ClassifyAsync_TransientFailure_IsRetriedAndSucceeds()
    {
        FakeAiProvider inner = new() { FailuresBeforeSuccess = 2 };

        AIClassificationResponse response = await Wrap(inner).ClassifyAsync(AiTestFactory.Request());

        response.Results.Should().NotBeEmpty();
        inner.ClassifyCallCount.Should().Be(3, "two failures then a success");
    }

    [Fact]
    public async Task ClassifyAsync_ExhaustedRetries_ThrowsTheLastFailure()
    {
        FakeAiProvider inner = new() { AlwaysFails = true };

        Func<Task> act = async () => await Wrap(inner).ClassifyAsync(AiTestFactory.Request());

        await act.Should().ThrowAsync<AIProviderException>();
        inner.ClassifyCallCount.Should().Be(3, "the first attempt plus two retries");
    }

    [Fact]
    public async Task ClassifyAsync_NoRetriesConfigured_CallsOnce()
    {
        FakeAiProvider inner = new() { AlwaysFails = true };

        Func<Task> act = async () => await Wrap(inner, o =>
        {
            o.Resilience.MaxRetries = 0;
            o.Resilience.BaseDelayMilliseconds = 1;
        }).ClassifyAsync(AiTestFactory.Request());

        await act.Should().ThrowAsync<AIProviderException>();
        inner.ClassifyCallCount.Should().Be(1);
    }

    [Fact]
    public async Task ClassifyAsync_AttemptExceedingTheTimeout_IsAbandoned()
    {
        FakeAiProvider inner = new() { Delay = TimeSpan.FromSeconds(30) };

        Func<Task> act = async () => await Wrap(inner, o =>
        {
            o.Resilience.MaxRetries = 0;
            o.Resilience.BaseDelayMilliseconds = 1;
            o.Resilience.PerAttemptTimeoutSeconds = 1;
        }).ClassifyAsync(AiTestFactory.Request());

        // A hung endpoint must surface as a provider failure, not as a hung conversion.
        await act.Should().ThrowAsync<AIProviderException>();
    }

    [Fact]
    public async Task ClassifyAsync_CallerCancellation_IsNotTreatedAsARetryableFailure()
    {
        FakeAiProvider inner = new() { Delay = TimeSpan.FromSeconds(10) };
        using CancellationTokenSource cts = new();
        cts.CancelAfter(TimeSpan.FromMilliseconds(50));

        Func<Task> act = async () => await Wrap(inner).ClassifyAsync(AiTestFactory.Request(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        inner.ClassifyCallCount.Should().Be(1, "a cancelled operation must not be retried");
    }

    [Fact]
    public async Task ProbeAsync_IsNotRetried()
    {
        FakeAiProvider inner = new() { Availability = AIProviderAvailability.Unavailable("down") };

        AIProviderAvailability availability = await Wrap(inner).ProbeAsync();

        availability.IsAvailable.Should().BeFalse();
        inner.ProbeCallCount.Should().Be(1, "a probe reports, it does not fail");
    }
}
