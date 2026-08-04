using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Models;

/// <summary>
/// A provider-agnostic classification request.
/// </summary>
/// <remarks>
/// The abstraction deliberately sits at the level of the <em>task</em> rather than the level of a
/// prompt. A prompt-shaped contract would be meaningless to a tensor-based provider such as ONNX
/// and would force prompt construction into the caller. Each provider translates this request into
/// whatever its engine needs, internally.
/// </remarks>
/// <param name="Subjects">The subjects to classify.</param>
/// <param name="Context">The label set and domain hints governing the task.</param>
public sealed record AIClassificationRequest(
    IReadOnlyList<ClassificationSubject> Subjects,
    ClassificationContext Context);
