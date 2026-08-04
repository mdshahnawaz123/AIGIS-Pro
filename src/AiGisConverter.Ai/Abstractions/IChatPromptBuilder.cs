using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// Builds the vendor-neutral prompt shared by every chat-based provider.
/// </summary>
/// <remarks>
/// Kept out of the individual providers so that OpenAI, Ollama, Claude and Gemini all ask the
/// same question and can be compared like for like. Only the wire format differs per provider.
/// </remarks>
public interface IChatPromptBuilder
{
    /// <summary>Builds a classification prompt for the supplied subjects.</summary>
    /// <param name="request">The classification task.</param>
    /// <param name="subjects">The slice of subjects to include, which may be a chunk of the request.</param>
    /// <returns>The prompt to send.</returns>
    ChatPrompt Build(AIClassificationRequest request, IReadOnlyList<ClassificationSubject> subjects);
}
