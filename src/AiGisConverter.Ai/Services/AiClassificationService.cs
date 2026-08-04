using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Caching;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Ai.Options;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Services;

/// <summary>
/// Adapts the provider pipeline to the domain port <see cref="IAiClassifier"/>.
/// </summary>
/// <remarks>
/// This is the only type in the layer that knows about business policy: the confidence threshold,
/// fallback behaviour, and the guarantee that every subject receives a result even when the model
/// declined to answer for it. Providers stay purely mechanical.
/// </remarks>
public sealed class AiClassificationService : IAiClassifier
{
    private readonly IAIProviderFactory _providerFactory;
    private readonly IOptionsMonitor<AiOptions> _options;
    private readonly ILogger<AiClassificationService> _logger;

    /// <summary>Initializes a new instance of the <see cref="AiClassificationService"/> class.</summary>
    /// <param name="providerFactory">Resolves the active and fallback providers.</param>
    /// <param name="options">Live AI layer options.</param>
    /// <param name="logger">Logger for the service.</param>
    public AiClassificationService(
        IAIProviderFactory providerFactory,
        IOptionsMonitor<AiOptions> options,
        ILogger<AiClassificationService> logger)
    {
        ArgumentNullException.ThrowIfNull(providerFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _providerFactory = providerFactory;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ClassificationResult>>> ClassifyAsync(
        IReadOnlyList<ClassificationSubject> subjects,
        ClassificationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subjects);
        ArgumentNullException.ThrowIfNull(context);

        if (subjects.Count == 0)
        {
            return Result.Success<IReadOnlyList<ClassificationResult>>([]);
        }

        AiOptions options = _options.CurrentValue;
        AIClassificationRequest request = new(subjects, context);

        IAIProvider active;

        try
        {
            active = _providerFactory.GetActiveProvider();
        }
        catch (AIProviderNotRegisteredException ex)
        {
            _logger.LogError(ex, "The configured AI provider is not registered.");
            return Result.Failure<IReadOnlyList<ClassificationResult>>(
                new Error("Ai.ProviderNotRegistered", ex.Message));
        }

        AIClassificationResponse? response =
            await TryClassifyAsync(active, request, options, cancellationToken).ConfigureAwait(false);

        if (response is null)
        {
            IAIProvider? fallback = _providerFactory.GetFallbackProvider();

            if (fallback is null || string.Equals(fallback.Key, active.Key, StringComparison.OrdinalIgnoreCase))
            {
                return Result.Failure<IReadOnlyList<ClassificationResult>>(new Error(
                    "Ai.ProviderUnavailable",
                    $"AI provider '{active.Key}' failed and no distinct fallback provider is configured."));
            }

            _logger.LogWarning(
                "Falling back from AI provider {ActiveProvider} to {FallbackProvider}.",
                active.Key,
                fallback.Key);

            response = await TryClassifyAsync(fallback, request, options, cancellationToken).ConfigureAwait(false);

            if (response is null)
            {
                return Result.Failure<IReadOnlyList<ClassificationResult>>(new Error(
                    "Ai.ClassificationFailed",
                    $"AI providers '{active.Key}' and '{fallback.Key}' both failed."));
            }
        }

        return Result.Success(Reconcile(subjects, context, response, options.ConfidenceThreshold));
    }

    /// <summary>Runs one provider, converting expected failures into a null response.</summary>
    private async Task<AIClassificationResponse?> TryClassifyAsync(
        IAIProvider provider,
        AIClassificationRequest request,
        AiOptions options,
        CancellationToken cancellationToken)
    {
        if (options.ProbeBeforeUse)
        {
            AIProviderAvailability availability =
                await provider.ProbeAsync(cancellationToken).ConfigureAwait(false);

            if (!availability.IsAvailable)
            {
                _logger.LogWarning(
                    "AI provider {ProviderKey} reported unavailable: {Reason}",
                    provider.Key,
                    availability.Reason);

                return null;
            }
        }

        try
        {
            return await provider.ClassifyAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (AIProviderException ex)
        {
            _logger.LogError(ex, "AI provider {ProviderKey} failed.", provider.Key);
            return null;
        }
    }

    /// <summary>
    /// Guarantees one result per subject and applies the acceptance threshold.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A provider that silently drops a subject must not silently drop a CAD layer from the
    /// conversion, so every subject gets a result whether the provider answered for it or not.
    /// </para>
    /// <para>
    /// Results are rebuilt rather than stamped in place. The response may have come from the
    /// shared cache, or from a provider that reuses instances between calls; either way, writing
    /// this caller's acceptance decision onto an object another caller also holds would corrupt
    /// it. Rebuilding costs one small object per subject and removes the whole class of problem.
    /// </para>
    /// </remarks>
    private static IReadOnlyList<ClassificationResult> Reconcile(
        IReadOnlyList<ClassificationSubject> subjects,
        ClassificationContext context,
        AIClassificationResponse response,
        double threshold)
    {
        Dictionary<string, ClassificationResult> byId =
            new(response.Results.Count, StringComparer.OrdinalIgnoreCase);

        foreach (ClassificationResult result in response.Results)
        {
            byId[result.SubjectId] = result;
        }

        List<ClassificationResult> reconciled = new(subjects.Count);

        foreach (ClassificationSubject subject in subjects)
        {
            if (byId.TryGetValue(subject.Id, out ClassificationResult? result))
            {
                reconciled.Add(ClassificationResponseCloner.CloneWithThreshold(result, threshold));
            }
            else
            {
                ClassificationResult missing = new(
                    subject.Id,
                    context.UnknownLabel,
                    Confidence.Zero,
                    response.ProviderKey)
                {
                    Rationale = "No classification was returned for this subject.",
                };

                missing.MarkAccepted(false);
                reconciled.Add(missing);
            }
        }

        return reconciled;
    }
}
