using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;

namespace AiGisConverter.Ai.Providers.Onnx;

/// <summary>
/// Default <see cref="IOnnxSessionProvider"/>. Loads the model and labels lazily and exactly once,
/// and reports failure through state rather than by throwing on construction.
/// </summary>
/// <remarks>
/// Failing softly matters here: a missing model file must not prevent the application from
/// starting. It simply makes this provider report unavailable so the fallback takes over.
/// </remarks>
public sealed class OnnxSessionProvider : IOnnxSessionProvider
{
    private readonly Lazy<LoadedModel> _model;
    private readonly ILogger<OnnxSessionProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="OnnxSessionProvider"/> class.</summary>
    /// <param name="options">Provider options.</param>
    /// <param name="logger">Logger for load diagnostics.</param>
    public OnnxSessionProvider(IOptions<OnnxOptions> options, ILogger<OnnxSessionProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _model = new Lazy<LoadedModel>(() => Load(options.Value), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <inheritdoc />
    public bool IsLoaded => _model.Value.Session is not null;

    /// <inheritdoc />
    public string? LoadFailureReason => _model.Value.FailureReason;

    /// <inheritdoc />
    public IReadOnlyList<string> Labels => _model.Value.Labels;

    /// <inheritdoc />
    public string InputName => _model.Value.InputName;

    /// <inheritdoc />
    public string OutputName => _model.Value.OutputName;

    /// <inheritdoc />
    public InferenceSession GetSession() =>
        _model.Value.Session
        ?? throw new InvalidOperationException(_model.Value.FailureReason ?? "The ONNX model is not loaded.");

    /// <inheritdoc />
    public void Dispose()
    {
        if (_model.IsValueCreated)
        {
            _model.Value.Session?.Dispose();
        }
    }

    private LoadedModel Load(OnnxOptions options)
    {
        string modelPath = ResolvePath(options.ModelPath);
        string labelsPath = ResolvePath(options.LabelsPath);

        if (!File.Exists(modelPath))
        {
            return LoadedModel.Failed($"ONNX model not found at '{modelPath}'.");
        }

        if (!File.Exists(labelsPath))
        {
            return LoadedModel.Failed($"ONNX label file not found at '{labelsPath}'.");
        }

        try
        {
            string[]? labels = JsonSerializer.Deserialize<string[]>(File.ReadAllText(labelsPath));

            if (labels is null || labels.Length == 0)
            {
                return LoadedModel.Failed($"ONNX label file '{labelsPath}' is empty or malformed.");
            }

            using SessionOptions sessionOptions = new()
            {
                IntraOpNumThreads = options.IntraOpNumThreads,
                GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
            };

            InferenceSession session = new(modelPath, sessionOptions);

            string inputName = string.IsNullOrWhiteSpace(options.InputTensorName)
                ? session.InputMetadata.Keys.First()
                : options.InputTensorName;

            string outputName = string.IsNullOrWhiteSpace(options.OutputTensorName)
                ? session.OutputMetadata.Keys.First()
                : options.OutputTensorName;

            _logger.LogInformation(
                "Loaded ONNX model '{ModelPath}' with {LabelCount} labels (input '{Input}', output '{Output}').",
                modelPath,
                labels.Length,
                inputName,
                outputName);

            return new LoadedModel(session, labels, inputName, outputName, null);
        }
        catch (OnnxRuntimeException ex)
        {
            _logger.LogError(ex, "ONNX Runtime failed to load '{ModelPath}'.", modelPath);
            return LoadedModel.Failed($"ONNX Runtime could not load '{modelPath}': {ex.Message}");
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "The ONNX label file '{LabelsPath}' is not valid JSON.", labelsPath);
            return LoadedModel.Failed($"The label file '{labelsPath}' is not valid JSON: {ex.Message}");
        }
        catch (IOException ex)
        {
            _logger.LogError(ex, "Failed to read ONNX assets.");
            return LoadedModel.Failed($"Failed to read ONNX assets: {ex.Message}");
        }
    }

    private static string ResolvePath(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);

    private sealed record LoadedModel(
        InferenceSession? Session,
        IReadOnlyList<string> Labels,
        string InputName,
        string OutputName,
        string? FailureReason)
    {
        public static LoadedModel Failed(string reason) =>
            new(null, [], string.Empty, string.Empty, reason);
    }
}
