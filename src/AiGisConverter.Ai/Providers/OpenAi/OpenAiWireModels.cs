using System.Text.Json.Serialization;

namespace AiGisConverter.Ai.Providers.OpenAi;

/// <summary>Request body for the OpenAI-compatible <c>/chat/completions</c> endpoint.</summary>
internal sealed record OpenAiChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public IReadOnlyList<OpenAiMessage> Messages { get; init; } = [];

    [JsonPropertyName("temperature")]
    public double Temperature { get; init; }

    [JsonPropertyName("response_format")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OpenAiResponseFormat? ResponseFormat { get; init; }
}

/// <summary>A single chat message.</summary>
internal sealed record OpenAiMessage
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;
}

/// <summary>Structured-output selector.</summary>
internal sealed record OpenAiResponseFormat
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "json_object";
}

/// <summary>Response body of the <c>/chat/completions</c> endpoint.</summary>
internal sealed record OpenAiChatResponse
{
    [JsonPropertyName("model")]
    public string? Model { get; init; }

    [JsonPropertyName("choices")]
    public IReadOnlyList<OpenAiChoice> Choices { get; init; } = [];

    [JsonPropertyName("usage")]
    public OpenAiUsage? Usage { get; init; }
}

/// <summary>One completion choice.</summary>
internal sealed record OpenAiChoice
{
    [JsonPropertyName("message")]
    public OpenAiMessage? Message { get; init; }

    [JsonPropertyName("finish_reason")]
    public string? FinishReason { get; init; }
}

/// <summary>Token accounting.</summary>
internal sealed record OpenAiUsage
{
    [JsonPropertyName("prompt_tokens")]
    public int? PromptTokens { get; init; }

    [JsonPropertyName("completion_tokens")]
    public int? CompletionTokens { get; init; }
}
