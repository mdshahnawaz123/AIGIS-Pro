namespace AiGisConverter.Ai.Models;

/// <summary>
/// Execution model of an AI provider. Used for capability decisions such as whether a network
/// round trip is involved, never for branching on a specific vendor.
/// </summary>
public enum AIProviderKind
{
    /// <summary>Deterministic, non-learned logic. Always available, no model artefacts.</summary>
    Deterministic = 0,

    /// <summary>A local model file executed in-process, for example ONNX Runtime.</summary>
    LocalModel = 1,

    /// <summary>A model served over HTTP on the local machine or LAN, for example Ollama or LM Studio.</summary>
    LocalService = 2,

    /// <summary>A hosted model reached over the public internet, for example OpenAI or Claude.</summary>
    RemoteService = 3,
}
