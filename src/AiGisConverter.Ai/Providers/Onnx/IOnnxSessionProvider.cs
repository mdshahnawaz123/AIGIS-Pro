using Microsoft.ML.OnnxRuntime;

namespace AiGisConverter.Ai.Providers.Onnx;

/// <summary>
/// Owns the lifetime of the ONNX inference session and its label vocabulary.
/// </summary>
/// <remarks>
/// Session construction memory-maps the model and allocates thread pools, so it is done once and
/// shared. Separating it from the provider keeps the provider unit-testable against a fake session.
/// </remarks>
public interface IOnnxSessionProvider : IDisposable
{
    /// <summary>Gets a value indicating whether the model and labels loaded successfully.</summary>
    bool IsLoaded { get; }

    /// <summary>Gets the reason loading failed, when <see cref="IsLoaded"/> is <see langword="false"/>.</summary>
    string? LoadFailureReason { get; }

    /// <summary>Gets the labels in model output order.</summary>
    IReadOnlyList<string> Labels { get; }

    /// <summary>Gets the resolved input tensor name.</summary>
    string InputName { get; }

    /// <summary>Gets the resolved output tensor name.</summary>
    string OutputName { get; }

    /// <summary>Gets the loaded session.</summary>
    /// <returns>The inference session.</returns>
    /// <exception cref="InvalidOperationException">The model failed to load.</exception>
    InferenceSession GetSession();
}
