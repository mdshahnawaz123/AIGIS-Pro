using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Driven port for AI-assisted classification of CAD content into GIS feature classes.
/// </summary>
/// <remarks>
/// The application layer depends on this abstraction only. Which engine actually answers
/// &#8212; ONNX, OpenAI, Ollama, or a deterministic rule set &#8212; is a composition-root concern.
/// </remarks>
public interface IAiClassifier
{
    /// <summary>Classifies a batch of subjects.</summary>
    /// <param name="subjects">The subjects to classify.</param>
    /// <param name="context">The label set and domain hints governing the task.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>
    /// A successful result containing one <see cref="ClassificationResult"/> per subject, or a
    /// failure describing why no classification could be produced.
    /// </returns>
    Task<Result<IReadOnlyList<ClassificationResult>>> ClassifyAsync(
        IReadOnlyList<ClassificationSubject> subjects,
        ClassificationContext context,
        CancellationToken cancellationToken = default);
}
