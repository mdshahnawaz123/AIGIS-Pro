namespace AiGisConverter.Ai.Models;

/// <summary>
/// A vendor-neutral chat prompt: a system instruction plus a user message.
/// </summary>
/// <remarks>
/// Every mainstream chat API &#8212; OpenAI, Azure OpenAI, Claude, Gemini, Ollama, LM Studio &#8212;
/// accepts this shape, so it can be built once and mapped by each provider's own wire adapter.
/// </remarks>
/// <param name="SystemMessage">Role and output-format instruction.</param>
/// <param name="UserMessage">The concrete task payload.</param>
public sealed record ChatPrompt(string SystemMessage, string UserMessage);
