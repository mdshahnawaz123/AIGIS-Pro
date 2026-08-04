namespace AiGisConverter.Ai.Models;

/// <summary>
/// Result of probing whether a provider can currently serve requests.
/// </summary>
/// <param name="IsAvailable">Whether the provider is ready.</param>
/// <param name="Reason">Why the provider is unavailable. <see langword="null"/> when available.</param>
/// <param name="ModelIdentifier">The model the provider resolved to, when it can report one.</param>
public sealed record AIProviderAvailability(bool IsAvailable, string? Reason, string? ModelIdentifier)
{
    /// <summary>Creates an available result.</summary>
    /// <param name="modelIdentifier">The resolved model identifier, when known.</param>
    /// <returns>An available <see cref="AIProviderAvailability"/>.</returns>
    public static AIProviderAvailability Available(string? modelIdentifier = null) =>
        new(true, null, modelIdentifier);

    /// <summary>Creates an unavailable result.</summary>
    /// <param name="reason">Why the provider cannot serve requests.</param>
    /// <returns>An unavailable <see cref="AIProviderAvailability"/>.</returns>
    public static AIProviderAvailability Unavailable(string reason) => new(false, reason, null);
}
