using System.Collections.Concurrent;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Factories;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Options;
using AiGisConverter.Ai.Services;
using AiGisConverter.Ai.Tests.TestSupport;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Ai;
using Microsoft.Extensions.Logging.Abstractions;

namespace AiGisConverter.Ai.Tests.Services;

public sealed class AiClassificationServiceTests
{
    private static AiClassificationService Create(
        IEnumerable<IAIProvider> providers,
        Action<AiOptions>? configure = null)
    {
        Microsoft.Extensions.Options.IOptionsMonitor<AiOptions> options = AiTestFactory.Options(configure);

        AIProviderFactory factory = new(
            [new ServiceProviderAIProviderSource(providers)],
            options,
            NullLogger<AIProviderFactory>.Instance);

        return new AiClassificationService(factory, options, NullLogger<AiClassificationService>.Instance);
    }

    [Fact]
    public async Task ClassifyAsync_NoSubjects_SucceedsWithNothing()
    {
        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([new FakeAiProvider()]).ClassifyAsync([], AiTestFactory.Context());

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEmpty();
    }

    [Fact]
    public async Task ClassifyAsync_AboveThreshold_IsAccepted()
    {
        AIClassificationResponse response = AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d));

        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([new FakeAiProvider(response: response)], o => o.ConfidenceThreshold = 0.65d)
                .ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.Value[0].IsAccepted.Should().BeTrue();
    }

    [Fact]
    public async Task ClassifyAsync_BelowThreshold_IsRetainedButNotAccepted()
    {
        AIClassificationResponse response = AiTestFactory.Response(AiTestFactory.Result("L1", 0.4d));

        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([new FakeAiProvider(response: response)], o => o.ConfidenceThreshold = 0.65d)
                .ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.Value.Should().ContainSingle("a low-confidence answer is for review, not for the bin");
        result.Value[0].IsAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_ExactlyAtThreshold_IsAccepted()
    {
        AIClassificationResponse response = AiTestFactory.Response(AiTestFactory.Result("L1", 0.65d));

        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([new FakeAiProvider(response: response)], o => o.ConfidenceThreshold = 0.65d)
                .ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.Value[0].IsAccepted.Should().BeTrue("the threshold is inclusive");
    }

    [Fact]
    public async Task ClassifyAsync_SubjectTheProviderIgnored_StillGetsAResult()
    {
        // A provider that drops a subject must not silently drop a CAD layer.
        AIClassificationResponse response = AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d));

        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([new FakeAiProvider(response: response)])
                .ClassifyAsync([AiTestFactory.Subject("L1"), AiTestFactory.Subject("L2", "C-STRM")],
                    AiTestFactory.Context());

        result.Value.Should().HaveCount(2);
        result.Value[1].Label.Should().Be("Unclassified");
        result.Value[1].IsAccepted.Should().BeFalse();
    }

    [Fact]
    public async Task ClassifyAsync_ActiveProviderUnavailable_FallsBack()
    {
        FakeAiProvider primary = new("primary") { Availability = AIProviderAvailability.Unavailable("offline") };
        FakeAiProvider fallback = new("fallback");

        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([primary, fallback], o =>
            {
                o.ActiveProvider = "primary";
                o.FallbackProvider = "fallback";
            }).ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.IsSuccess.Should().BeTrue();
        result.Value[0].ProviderKey.Should().Be("fallback");
        primary.ClassifyCallCount.Should().Be(0, "an unavailable provider is not called");
    }

    [Fact]
    public async Task ClassifyAsync_ActiveProviderThrows_FallsBack()
    {
        FakeAiProvider primary = new("primary") { AlwaysFails = true };
        FakeAiProvider fallback = new("fallback");

        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([primary, fallback], o =>
            {
                o.ActiveProvider = "primary";
                o.FallbackProvider = "fallback";
            }).ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.IsSuccess.Should().BeTrue();
        result.Value[0].ProviderKey.Should().Be("fallback");
    }

    [Fact]
    public async Task ClassifyAsync_BothProvidersFail_ReportsFailure()
    {
        Result<IReadOnlyList<ClassificationResult>> result =
            await Create(
                [new FakeAiProvider("primary") { AlwaysFails = true },
                 new FakeAiProvider("fallback") { AlwaysFails = true }],
                o =>
                {
                    o.ActiveProvider = "primary";
                    o.FallbackProvider = "fallback";
                }).ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Ai.ClassificationFailed");
    }

    [Fact]
    public async Task ClassifyAsync_FallbackDisabled_SurfacesTheFailure()
    {
        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([new FakeAiProvider("primary") { AlwaysFails = true }], o =>
            {
                o.ActiveProvider = "primary";
                o.FallbackProvider = AiOptions.DisabledFallback;
            }).ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("Ai.ProviderUnavailable");
    }

    [Fact]
    public async Task ClassifyAsync_ConfiguredProviderMissing_FailsWithAnActionableError()
    {
        Result<IReadOnlyList<ClassificationResult>> result =
            await Create([new FakeAiProvider("fake")], o => o.ActiveProvider = "does-not-exist")
                .ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        result.IsFailure.Should().BeTrue();
        result.Error.Message.Should().Contain("fake", "the message must name what is registered");
    }

    [Fact]
    public async Task ClassifyAsync_Cancellation_Propagates()
    {
        using CancellationTokenSource cts = new();
        await cts.CancelAsync();

        Func<Task> act = async () => await Create([new FakeAiProvider()])
            .ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context(), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task ClassifyAsync_ProbeDisabled_SkipsTheProbe()
    {
        FakeAiProvider provider = new();

        await Create([provider], o => o.ProbeBeforeUse = false)
            .ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        provider.ProbeCallCount.Should().Be(0);
    }

    [Fact]
    public async Task ClassifyAsync_DoesNotMutateTheProvidersResponse()
    {
        // The regression that started this: the service used to stamp acceptance onto the very
        // objects the provider returned, which are the cache's objects on a hit.
        AIClassificationResponse shared = AiTestFactory.Response(AiTestFactory.Result("L1", 0.9d));

        await Create([new FakeAiProvider(response: shared)], o => o.ConfidenceThreshold = 0.1d)
            .ClassifyAsync([AiTestFactory.Subject("L1")], AiTestFactory.Context());

        shared.Results[0].IsAccepted.Should().BeFalse("the provider's instance must come back untouched");
    }

    [Fact]
    public async Task ClassifyAsync_ConcurrentCallersWithDifferentThresholds_DoNotCorruptEachOther()
    {
        // Two callers, one provider instance handing back the same objects every time, thresholds
        // on either side of the confidence. Before the fix these raced on IsAccepted.
        AIClassificationResponse shared = AiTestFactory.Response(
            AiTestFactory.Result("L1", 0.5d),
            AiTestFactory.Result("L2", 0.5d));

        IReadOnlyList<ClassificationSubject> subjects =
            [AiTestFactory.Subject("L1"), AiTestFactory.Subject("L2", "C-STRM")];

        AiClassificationService strict = Create(
            [new FakeAiProvider(response: shared)], o => o.ConfidenceThreshold = 0.9d);

        AiClassificationService lenient = Create(
            [new FakeAiProvider(response: shared)], o => o.ConfidenceThreshold = 0.1d);

        ConcurrentBag<bool> strictFlags = [];
        ConcurrentBag<bool> lenientFlags = [];

        await Task.WhenAll(Enumerable.Range(0, 200).Select(async i =>
        {
            if (i % 2 == 0)
            {
                Result<IReadOnlyList<ClassificationResult>> r =
                    await strict.ClassifyAsync(subjects, AiTestFactory.Context());

                foreach (ClassificationResult result in r.Value)
                {
                    strictFlags.Add(result.IsAccepted);
                }
            }
            else
            {
                Result<IReadOnlyList<ClassificationResult>> r =
                    await lenient.ClassifyAsync(subjects, AiTestFactory.Context());

                foreach (ClassificationResult result in r.Value)
                {
                    lenientFlags.Add(result.IsAccepted);
                }
            }
        }));

        strictFlags.Should().OnlyContain(accepted => accepted == false, "0.5 is below the 0.9 threshold");
        lenientFlags.Should().OnlyContain(accepted => accepted == true, "0.5 is above the 0.1 threshold");
    }

    [Fact]
    public async Task ClassifyAsync_ParallelCallsOverManySubjects_AllReturnCompleteResults()
    {
        IReadOnlyList<ClassificationSubject> subjects = AiTestFactory.Subjects(50);
        AiClassificationService service = Create([new FakeAiProvider()]);

        IReadOnlyList<ClassificationResult>[] runs = await Task.WhenAll(
            Enumerable.Range(0, 16).Select(async _ =>
                (await service.ClassifyAsync(subjects, AiTestFactory.Context())).Value));

        runs.Should().OnlyContain(r => r.Count == 50);
        runs.Should().OnlyContain(r => r.Select(x => x.SubjectId).Distinct().Count() == 50);
    }
}
