using System.Text.Json.Serialization;

namespace AiGisConverter.Ai.Providers.Ollama;

/// <summary>Request body for the Ollama <c>/api/chat</c> endpoint.</summary>
internal sealed record OllamaChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public IReadOnlyList<OllamaMessage> Messages { get; init; } = [];

    [JsonPropertyName("stream")]
    public bool Stream { get; init; }

    [JsonPropertyName("format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Format { get; init; }

    [JsonPropertyName("keep_alive")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? KeepAlive { get; init; }

    [JsonPropertyName("options")]
    public OllamaRequestOptions Options { get; init; } = new();
}

/// <summary>A single chat message.</summary>
internal sealed record OllamaMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

/// <summary>Sampling options.</summary>
internal sealed record OllamaRequestOptions
{
    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }
}

/// <summary>Response body of the Ollama <c>/api/chat</c> endpoint.</summary>
internal sealed record OllamaChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("message")]
    public OllamaMessage? Message { get; init; }

    [JsonPropertyName("prompt_eval_count")]
    public int? PromptEvalCount { get; init; }

    [JsonPropertyName("eval_count")]
    public int? EvalCount { get; init; }
}

/// <summary>Response body of the Ollama <c>/api/tags</c> endpoint.</summary>
internal sealed record OllamaTagsResponse
{
    [JsonPropertyName("models")]
    public IReadOnlyList<OllamaTag> Models { get; init; } = [];
}

/// <summary>A locally installed model.</summary>
internal sealed record OllamaTag
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;
}
