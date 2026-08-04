using System.Diagnostics;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace AiGisConverter.Ai.Providers.Onnx;

/// <summary>
/// Classifies with a local ONNX model executed in-process. Fully offline, no prompt, no tokens.
/// </summary>
/// <remarks>
/// <para>
/// This provider is the reason <see cref="IAIProvider"/> is defined in terms of a classification
/// <em>task</em> rather than a prompt. It never sees a <see cref="ChatPrompt"/>: it encodes each
/// subject into a feature vector, runs the graph and applies a softmax.
/// </para>
/// <para>
/// The model's label space and the caller's candidate label set need not agree. Scores are
/// restricted to their intersection and renormalised, so one shipped model can serve several
/// project-specific label schemas.
/// </para>
/// </remarks>
public sealed class OnnxProvider : IAIProvider
{
    /// <summary>The configuration key and provider key for this provider.</summary>
    public const string ProviderKey = "onnx";

    private const int MaximumAlternatives = 3;

    private readonly IOnnxSessionProvider _sessionProvider;
    private readonly HashingFeatureEncoder _encoder;
    private readonly IOptionsMonitor<OnnxOptions> _options;
    private readonly ILogger<OnnxProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="OnnxProvider"/> class.</summary>
    /// <param name="sessionProvider">Owns the inference session and labels.</param>
    /// <param name="encoder">Encodes subjects into feature vectors.</param>
    /// <param name="options">Live provider options.</param>
    /// <param name="logger">Logger for the provider.</param>
    public OnnxProvider(
        IOnnxSessionProvider sessionProvider,
        HashingFeatureEncoder encoder,
        IOptionsMonitor<OnnxOptions> options,
        ILogger<OnnxProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(sessionProvider);
        ArgumentNullException.ThrowIfNull(encoder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _sessionProvider = sessionProvider;
        _encoder = encoder;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Key => ProviderKey;

    /// <inheritdoc />
    public AIProviderMetadata Metadata { get; } = new(
        ProviderKey,
        "ONNX Runtime (local model)",
        AIProviderKind.LocalModel,
        MaxSubjectsPerCall: int.MaxValue,
        SupportsRationale: false,
        RequiresNetwork: false);

    /// <inheritdoc />
    public Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_sessionProvider.IsLoaded
            ? AIProviderAvailability.Available($"onnx ({_sessionProvider.Labels.Count} labels)")
            : AIProviderAvailability.Unavailable(_sessionProvider.LoadFailureReason ?? "The ONNX model is not loaded."));

    /// <inheritdoc />
    public Task<AIClassificationResponse> ClassifyAsync(
        AIClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_sessionProvider.IsLoaded)
        {
            throw new AIProviderException(
                Key,
                _sessionProvider.LoadFailureReason ?? "The ONNX model is not loaded.");
        }

        long startedAt = Stopwatch.GetTimestamp();
        OnnxOptions options = _options.CurrentValue;
        InferenceSession session = _sessionProvider.GetSession();
        IReadOnlyList<string> modelLabels = _sessionProvider.Labels;

        int[] allowedIndices = ResolveAllowedIndices(modelLabels, request.Context.CandidateLabels);

        if (allowedIndices.Length == 0)
        {
            _logger.LogWarning(
                "The ONNX model's label space does not intersect the {LabelCount} requested candidate labels.",
                request.Context.CandidateLabels.Count);
        }

        List<ClassificationResult> results = new(request.Subjects.Count);

        foreach (ClassificationSubject subject in request.Subjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(ClassifyOne(session, subject, request.Context, modelLabels, allowedIndices, options));
        }

        AIUsage usage = new(null, null, Stopwatch.GetElapsedTime(startedAt));
        return Task.FromResult(new AIClassificationResponse(results, Key, "onnx", usage));
    }

    private ClassificationResult ClassifyOne(
        InferenceSession session,
        ClassificationSubject subject,
        ClassificationContext context,
        IReadOnlyList<string> modelLabels,
        int[] allowedIndices,
        OnnxOptions options)
    {
        if (allowedIndices.Length == 0)
        {
            return new ClassificationResult(subject.Id, context.UnknownLabel, Confidence.Zero, Key);
        }

        float[] scores = Infer(session, subject, options);

        if (options.ApplySoftmax)
        {
            scores = Softmax(scores, allowedIndices);
        }

        List<ClassificationCandidate> ranked = allowedIndices
            .Where(index => index < scores.Length)
            .Select(index => new ClassificationCandidate(modelLabels[index], Confidence.Clamp(scores[index])))
            .OrderByDescending(candidate => candidate.Confidence.Value)
            .ThenBy(candidate => candidate.Label, StringComparer.Ordinal)
            .ToList();

        if (ranked.Count == 0)
        {
            return new ClassificationResult(subject.Id, context.UnknownLabel, Confidence.Zero, Key);
        }

        ClassificationResult result = new(subject.Id, ranked[0].Label, ranked[0].Confidence, Key);

        for (int i = 1; i < Math.Min(ranked.Count, MaximumAlternatives + 1); i++)
        {
            result.AddAlternative(ranked[i]);
        }

        return result;
    }

    private float[] Infer(InferenceSession session, ClassificationSubject subject, OnnxOptions options)
    {
        float[] features = _encoder.Encode(subject, options.FeatureDimension);
        DenseTensor<float> tensor = new(features, [1, options.FeatureDimension]);

        List<NamedOnnxValue> inputs =
            [NamedOnnxValue.CreateFromTensor(_sessionProvider.InputName, tensor)];

        try
        {
            using IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs = session.Run(inputs);

            DisposableNamedOnnxValue? output = outputs
                .FirstOrDefault(o => string.Equals(o.Name, _sessionProvider.OutputName, StringComparison.Ordinal))
                ?? outputs.FirstOrDefault();

            return output is null
                ? throw new AIProviderException(Key, "The ONNX model produced no output tensor.")
                : output.AsEnumerable<float>().ToArray();
        }
        catch (OnnxRuntimeException ex)
        {
            throw new AIProviderException(
                Key,
                $"ONNX inference failed. Confirm 'FeatureDimension' ({options.FeatureDimension}) " +
                "matches the model's expected input shape.",
                ex);
        }
    }

    /// <summary>Maps the caller's candidate labels onto the model's output indices.</summary>
    private static int[] ResolveAllowedIndices(
        IReadOnlyList<string> modelLabels,
        IReadOnlyList<string> candidateLabels)
    {
        HashSet<string> candidates = new(candidateLabels, StringComparer.OrdinalIgnoreCase);
        List<int> indices = [];

        for (int i = 0; i < modelLabels.Count; i++)
        {
            if (candidates.Contains(modelLabels[i]))
            {
                indices.Add(i);
            }
        }

        return [.. indices];
    }

    /// <summary>Softmax restricted to the allowed indices, so probabilities sum to one over them.</summary>
    private static float[] Softmax(float[] logits, int[] allowedIndices)
    {
        float max = float.NegativeInfinity;

        foreach (int index in allowedIndices)
        {
            if (index < logits.Length && logits[index] > max)
            {
                max = logits[index];
            }
        }

        if (float.IsNegativeInfinity(max))
        {
            return logits;
        }

        double sum = 0d;
        float[] result = new float[logits.Length];

        foreach (int index in allowedIndices)
        {
            if (index >= logits.Length)
            {
                continue;
            }

            double exponent = Math.Exp(logits[index] - max);
            result[index] = (float)exponent;
            sum += exponent;
        }

        if (sum <= double.Epsilon)
        {
            return result;
        }

        foreach (int index in allowedIndices)
        {
            if (index < result.Length)
            {
                result[index] = (float)(result[index] / sum);
            }
        }

        return result;
    }
}
