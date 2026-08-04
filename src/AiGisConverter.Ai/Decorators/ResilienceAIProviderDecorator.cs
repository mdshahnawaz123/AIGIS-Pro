using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Decorators;

/// <summary>
/// Adds a per-attempt timeout and bounded exponential backoff with jitter to every provider.
/// </summary>
/// <remarks>
/// Applied centrally so that a new provider is resilient the moment it is registered, and so the
/// retry policy is configured in one place rather than duplicated per vendor SDK.
/// </remarks>
public sealed class ResilienceAIProviderDecorator : IAIProviderDecorator
{
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes a new instance of the <see cref="ResilienceAIProviderDecorator"/> class.</summary>
    /// <param name="options">Live AI layer options.</param>
    /// <param name="loggerFactory">Factory used to create a logger per wrapped provider.</param>
    public ResilienceAIProviderDecorator(IOptionsMonitor<AiOptions> options, ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(loggerFactory);

        _options = options;
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public int Order => 10;

    /// <inheritdoc />
    public IAIProvider Decorate(IAIProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);

        return new ResilientProvider(
            inner,
            _options,
            _loggerFactory.CreateLogger($"AiGisConverter.Ai.Resilience.{inner.Key}"));
    }

    private sealed class ResilientProvider : IAIProvider
    {
        private readonly IAIProvider _inner;
        private readonly IOptionsMonitor<AiOptions> _options;
        private readonly ILogger _logger;

        public ResilientProvider(IAIProvider inner, IOptionsMonitor<AiOptions> options, ILogger logger)
        {
            _inner = inner;
            _options = options;
            _logger = logger;
        }

        public string Key => _inner.Key;

        public AIProviderMetadata Metadata => _inner.Metadata;

        public Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default) =>
            _inner.ProbeAsync(cancellationToken);

        public async Task<AIClassificationResponse> ClassifyAsync(
            AIClassificationRequest request,
            CancellationToken cancellationToken = default)
        {
            AiResilienceOptions policy = _options.CurrentValue.Resilience;
            AIProviderException? lastFailure = null;

            for (int attempt = 0; attempt <= policy.MaxRetries; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                using CancellationTokenSource attemptCts =
                    CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                attemptCts.CancelAfter(TimeSpan.FromSeconds(policy.PerAttemptTimeoutSeconds));

                try
                {
                    return await _inner.ClassifyAsync(request, attemptCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (OperationCanceledException ex)
                {
                    lastFailure = new AIProviderException(
                        Key,
                        $"Provider '{Key}' exceeded the {policy.PerAttemptTimeoutSeconds}s attempt timeout.",
                        ex);
                }
                catch (AIProviderException ex)
                {
                    lastFailure = ex;
                }

                if (attempt < policy.MaxRetries)
                {
                    TimeSpan delay = ComputeDelay(policy, attempt);

                    _logger.LogWarning(
                        lastFailure,
                        "Attempt {Attempt}/{MaxAttempts} against {ProviderKey} failed. Retrying in {DelayMs} ms.",
                        attempt + 1,
                        policy.MaxRetries + 1,
                        Key,
                        delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
                }
            }

            throw lastFailure ?? new AIProviderException(Key, $"Provider '{Key}' failed.");
        }

        /// <summary>Computes an exponential backoff delay with full jitter.</summary>
        private static TimeSpan ComputeDelay(AiResilienceOptions policy, int attempt)
        {
            double exponential = policy.BaseDelayMilliseconds * Math.Pow(2d, attempt);
            double jittered = Random.Shared.NextDouble() * exponential;

            return TimeSpan.FromMilliseconds(Math.Min(jittered + policy.BaseDelayMilliseconds, 30_000d));
        }
    }
}
