using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;

namespace AiGisConverter.Ai.Tests.TestSupport;

/// <summary>
/// A provider whose behaviour the test dictates: what it returns, how it fails, how slowly.
/// </summary>
/// <remarks>
/// Deliberately returns the <em>same</em> response instance on every call unless told otherwise.
/// That is what a cache does, and what a provider reusing an internal buffer would do, so it is
/// the condition under which the shared-mutable-state defect appeared.
/// </remarks>
internal sealed class FakeAiProvider : IAIProvider
{
    private readonly AIClassificationResponse? _response;

    public FakeAiProvider(string key = "fake", AIClassificationResponse? response = null)
    {
        Key = key;
        _response = response;
    }

    public string Key { get; }

    public AIProviderMetadata Metadata => new(
        Key,
        Key,
        AIProviderKind.Deterministic,
        MaxSubjectsPerCall: int.MaxValue,
        SupportsRationale: true,
        RequiresNetwork: false);

    private int _classifyCallCount;
    private int _probeCallCount;

    /// <summary>Gets how many times classification was attempted.</summary>
    public int ClassifyCallCount => Volatile.Read(ref _classifyCallCount);

    /// <summary>Gets how many times the provider was probed.</summary>
    public int ProbeCallCount => Volatile.Read(ref _probeCallCount);

    /// <summary>Gets or sets the availability the probe reports.</summary>
    public AIProviderAvailability Availability { get; set; } = AIProviderAvailability.Available("fake");

    /// <summary>Gets or sets how many leading calls throw before one succeeds.</summary>
    public int FailuresBeforeSuccess { get; set; }

    /// <summary>Gets or sets a value indicating whether every call throws.</summary>
    public bool AlwaysFails { get; set; }

    /// <summary>Gets or sets a delay applied before each call returns.</summary>
    public TimeSpan Delay { get; set; } = TimeSpan.Zero;

    public Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _probeCallCount);

        return Task.FromResult(Availability);
    }

    public async Task<AIClassificationResponse> ClassifyAsync(
        AIClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        int attempt = Interlocked.Increment(ref _classifyCallCount);

        if (Delay > TimeSpan.Zero)
        {
            await Task.Delay(Delay, cancellationToken).ConfigureAwait(false);
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (AlwaysFails || attempt <= FailuresBeforeSuccess)
        {
            throw new AIProviderException(Key, $"deliberate failure {attempt}");
        }

        return _response ?? AiTestFactory.Response(
            [.. request.Subjects.Select(s => AiTestFactory.Result(s.Id, 0.8d, providerKey: Key))]);
    }
}
