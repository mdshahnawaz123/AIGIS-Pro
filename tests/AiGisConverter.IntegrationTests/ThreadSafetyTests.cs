using System.Collections.Concurrent;
using AiGisConverter.Ai.Caching;
using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.IntegrationTests;

/// <summary>
/// Concurrency guarantees for the state that is genuinely shared.
/// </summary>
/// <remarks>
/// Most of this application is shared-nothing: a conversion run owns its own document, its own
/// geometry and its own writer. The AI response cache is the exception — it is a singleton that
/// every concurrent conversion reads and writes. That makes it the only object in the solution
/// where a data race is plausible, so it is the one tested hardest here.
/// <para>
/// These tests use the real clock deliberately: none of them exercise expiry, and substituting a
/// controllable clock would only add a synchronisation point that masks the races being hunted.
/// </para>
/// </remarks>
public sealed class ThreadSafetyTests
{
    private static AIClassificationResponse Response(string label, double score) =>
        new(
            [new ClassificationResult("subject-1", label, Confidence.FromScore(score), "test")],
            "test",
            "model-1",
            new AIUsage(10, 5, TimeSpan.FromMilliseconds(1)));

    [Fact]
    public void Cache_DoesNotHandOutTheStoredInstance()
    {
        // The bug this guards: a caller that mutates a cached response corrupts it for every
        // later caller. Reference equality failing here is the whole point of the test.
        InMemoryAIResponseCache cache = new(TimeProvider.System);
        AIClassificationResponse stored = Response("Road", 0.9);

        cache.Set("k", stored, TimeSpan.FromMinutes(5));
        cache.TryGet("k", out AIClassificationResponse? first).Should().BeTrue();
        cache.TryGet("k", out AIClassificationResponse? second).Should().BeTrue();

        first.Should().NotBeSameAs(stored);
        first.Should().NotBeSameAs(second);
        first!.Results[0].Should().NotBeSameAs(stored.Results[0]);
        first.Results[0].Label.Should().Be("Road");
    }

    [Fact]
    public async Task Cache_SurvivesInterleavedReadsAndWritesWithoutTearing()
    {
        InMemoryAIResponseCache cache = new(TimeProvider.System);
        ConcurrentBag<string> observed = [];
        ConcurrentBag<Exception> failures = [];

        // Two writers alternate between two distinguishable values while readers observe.
        // A torn read shows up as a label that is neither of the two written values.
        await Parallel.ForEachAsync(
            Enumerable.Range(0, 200),
            async (index, token) =>
            {
                try
                {
                    if (index % 2 == 0)
                    {
                        cache.Set("shared", Response(index % 4 == 0 ? "Road" : "Parcel", 0.9), TimeSpan.FromMinutes(5));
                    }
                    else if (cache.TryGet("shared", out AIClassificationResponse? read) && read is not null)
                    {
                        observed.Add(read.Results[0].Label);
                    }

                    await Task.Yield();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            });

        failures.Should().BeEmpty();
        observed.Should().OnlyContain(label => label == "Road" || label == "Parcel");
    }

    [Fact]
    public async Task Cache_ClearDuringConcurrentAccessDoesNotThrow()
    {
        InMemoryAIResponseCache cache = new(TimeProvider.System);
        ConcurrentBag<Exception> failures = [];

        await Parallel.ForEachAsync(
            Enumerable.Range(0, 300),
            async (index, token) =>
            {
                try
                {
                    switch (index % 3)
                    {
                        case 0: cache.Set($"k{index % 7}", Response("Road", 0.5), TimeSpan.FromMinutes(1)); break;
                        case 1: cache.TryGet($"k{index % 7}", out _); break;
                        default: cache.Clear(); break;
                    }

                    await Task.Yield();
                }
                catch (Exception exception)
                {
                    failures.Add(exception);
                }
            });

        failures.Should().BeEmpty("Clear must not race with in-flight reads");
    }

    [Fact]
    public async Task ConcurrentExports_ToDistinctPathsDoNotInterfere()
    {
        string root = Directory.CreateTempSubdirectory("aigis-concurrent").FullName;

        try
        {
            await Parallel.ForEachAsync(
                Enumerable.Range(0, 32),
                async (index, token) =>
                    await File.WriteAllTextAsync(Path.Combine(root, $"layer-{index}.txt"), $"payload-{index}", token));

            Directory.GetFiles(root).Should().HaveCount(32);

            foreach (string path in Directory.GetFiles(root))
            {
                string expected = $"payload-{Path.GetFileNameWithoutExtension(path).Split('-')[1]}";
                (await File.ReadAllTextAsync(path)).Should().Be(expected);
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
