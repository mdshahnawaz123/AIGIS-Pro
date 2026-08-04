using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Models;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Ai.Decorators;

/// <summary>
/// Applies structured logging to every provider, outermost in the pipeline so it observes the
/// full cost including retries and cache lookups.
/// </summary>
public sealed class LoggingAIProviderDecorator : IAIProviderDecorator
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>Initializes a new instance of the <see cref="LoggingAIProviderDecorator"/> class.</summary>
    /// <param name="loggerFactory">Factory used to create a logger per wrapped provider.</param>
    public LoggingAIProviderDecorator(ILoggerFactory loggerFactory)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        _loggerFactory = loggerFactory;
    }

    /// <inheritdoc />
    public int Order => 0;

    /// <inheritdoc />
    public IAIProvider Decorate(IAIProvider inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        return new LoggingProvider(inner, _loggerFactory.CreateLogger($"AiGisConverter.Ai.Provider.{inner.Key}"));
    }

    private sealed class LoggingProvider : IAIProvider
    {
        private readonly IAIProvider _inner;
        private readonly ILogger _logger;

        public LoggingProvider(IAIProvider inner, ILogger logger)
        {
            _inner = inner;
            _logger = logger;
        }

        public string Key => _inner.Key;

        public AIProviderMetadata Metadata => _inner.Metadata;

        public async Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default)
        {
            AIProviderAvailability availability = await _inner.ProbeAsync(cancellationToken).ConfigureAwait(false);

            if (availability.IsAvailable)
            {
                _logger.LogInformation(
                    "AI provider {ProviderKey} is available (model {Model}).",
                    Key,
                    availability.ModelIdentifier ?? "unspecified");
            }
            else
            {
                _logger.LogWarning("AI provider {ProviderKey} is unavailable: {Reason}", Key, availability.Reason);
            }

            return availability;
        }

        public async Task<AIClassificationResponse> ClassifyAsync(
            AIClassificationRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);

            _logger.LogInformation(
                "Classifying {SubjectCount} subjects against {LabelCount} labels using {ProviderKey}.",
                request.Subjects.Count,
                request.Context.CandidateLabels.Count,
                Key);

            try
            {
                AIClassificationResponse response =
                    await _inner.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);

                _logger.LogInformation(
                    "Provider {ProviderKey} classified {ResultCount}/{SubjectCount} subjects in {ElapsedMs} ms " +
                    "(prompt tokens {PromptTokens}, completion tokens {CompletionTokens}).",
                    Key,
                    response.Results.Count,
                    request.Subjects.Count,
                    response.Usage.Duration.TotalMilliseconds,
                    response.Usage.PromptTokens,
                    response.Usage.CompletionTokens);

                return response;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Classification with provider {ProviderKey} was cancelled.", Key);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Classification with provider {ProviderKey} failed.", Key);
                throw;
            }
        }
    }
}
