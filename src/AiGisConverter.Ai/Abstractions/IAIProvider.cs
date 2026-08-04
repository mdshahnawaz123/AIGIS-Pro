using AiGisConverter.Ai.Models;

namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// The single extension point of the AI layer. One implementation per AI engine.
/// </summary>
/// <remarks>
/// <para>
/// Adding a new engine &#8212; Azure OpenAI, Claude, Gemini, Hugging Face, LM Studio &#8212; means
/// adding one class that implements this interface and one registration extension method.
/// No existing type is edited, which is what makes the layer open for extension and closed
/// for modification.
/// </para>
/// <para>
/// All engine-specific concerns live behind this boundary: wire formats, authentication,
/// prompt dialects, tensor shapes, tokenisation and error taxonomies. Nothing outside a
/// provider assembly folder may branch on <see cref="Key"/>.
/// </para>
/// </remarks>
public interface IAIProvider
{
    /// <summary>
    /// Gets the unique, lower-case key identifying this provider. Matched case-insensitively
    /// against <c>Ai:ActiveProvider</c> in configuration.
    /// </summary>
    string Key { get; }

    /// <summary>Gets the static description of this provider.</summary>
    AIProviderMetadata Metadata { get; }

    /// <summary>
    /// Checks whether the provider can currently serve requests: model file present, endpoint
    /// reachable, credentials resolvable.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The availability of the provider. Implementations must not throw for the
    /// expected unavailable cases; they report them through the returned value.</returns>
    Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Classifies a batch of subjects.</summary>
    /// <param name="request">The classification task.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>One result per subject the provider could classify.</returns>
    /// <exception cref="Exceptions.AIProviderException">The engine failed irrecoverably.</exception>
    Task<AIClassificationResponse> ClassifyAsync(
        AIClassificationRequest request,
        CancellationToken cancellationToken = default);
}
