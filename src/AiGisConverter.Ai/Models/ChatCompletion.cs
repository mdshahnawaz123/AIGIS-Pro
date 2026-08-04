namespace AiGisConverter.Ai.Models;

/// <summary>
/// The normalised outcome of a chat completion call, stripped of vendor envelope.
/// </summary>
/// <param name="Content">The assistant message content.</param>
/// <param name="ModelIdentifier">The model the service reports having used.</param>
/// <param name="PromptTokens">Tokens consumed by the input, when reported.</param>
/// <param name="CompletionTokens">Tokens produced, when reported.</param>
public sealed record ChatCompletion(
    string Content,
    string? ModelIdentifier,
    int? PromptTokens,
    int? CompletionTokens);
