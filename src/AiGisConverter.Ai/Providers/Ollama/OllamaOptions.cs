using AiGisConverter.Ai.Options;

namespace AiGisConverter.Ai.Providers.Ollama;

/// <summary>
/// Options for <see cref="OllamaProvider"/>, bound from <c>Ai:Providers:ollama</c>.
/// </summary>
public sealed class OllamaOptions : ChatProviderOptions
{
    /// <summary>Initializes a new instance of the <see cref="OllamaOptions"/> class.</summary>
    public OllamaOptions()
    {
        Endpoint = new Uri("http://localhost:11434");
        Model = "llama3.1";
        MaxSubjectsPerCall = 20;
    }

    /// <summary>
    /// Gets or sets how long Ollama keeps the model resident after a request, for example
    /// <c>5m</c>. Keeping the model loaded avoids a multi-second reload per batch.
    /// </summary>
    public string KeepAlive { get; set; } = "5m";

    /// <summary>
    /// Gets or sets a value indicating whether the model is asked to constrain its output to JSON.
    /// Disable only for models that do not support Ollama's <c>format</c> parameter.
    /// </summary>
    public bool UseJsonFormat { get; set; } = true;
}
