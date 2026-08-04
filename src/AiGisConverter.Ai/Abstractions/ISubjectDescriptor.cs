using AiGisConverter.Domain.Entities.Ai;

namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// Renders a <see cref="ClassificationSubject"/> as a compact, deterministic text descriptor.
/// </summary>
/// <remarks>
/// Shared by the prompt builder, the response cache key and the deterministic providers, so that
/// all three see exactly the same view of a subject.
/// </remarks>
public interface ISubjectDescriptor
{
    /// <summary>Renders a subject as a single-line descriptor.</summary>
    /// <param name="subject">The subject to render.</param>
    /// <returns>A stable textual description.</returns>
    string Describe(ClassificationSubject subject);
}
