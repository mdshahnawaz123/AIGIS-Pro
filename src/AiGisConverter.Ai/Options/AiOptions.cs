using System.ComponentModel.DataAnnotations;

namespace AiGisConverter.Ai.Options;

/// <summary>
/// Core AI layer configuration, bound from the <c>Ai</c> section.
/// </summary>
/// <remarks>
/// This type contains no provider-specific setting and no provider key constant. Each provider
/// binds its own options from <c>Ai:Providers:&lt;key&gt;</c> inside its own registration
/// extension, so adding a provider never widens this class.
/// </remarks>
public sealed class AiOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Ai";

    /// <summary>Configuration section under which each provider binds its own options.</summary>
    public const string ProvidersSectionName = "Ai:Providers";

    /// <summary>
    /// Gets or sets the key of the provider that serves requests. Leave empty to let the factory
    /// select the least-demanding registered provider, so that no provider key is hard-coded here.
    /// </summary>
    public string ActiveProvider { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the key of the provider used when the active provider is unavailable or fails.
    /// Leave empty to let the factory select the least-demanding registered provider. Set to
    /// <see cref="DisabledFallback"/> to surface the failure instead of degrading.
    /// </summary>
    public string FallbackProvider { get; set; } = string.Empty;

    /// <summary>Value of <see cref="FallbackProvider"/> that disables fallback entirely.</summary>
    public const string DisabledFallback = "none";

    /// <summary>
    /// Gets or sets the minimum confidence at which a result is marked accepted. Lower-scoring
    /// results are retained and flagged for human review rather than discarded.
    /// </summary>
    [Range(0d, 1d)]
    public double ConfidenceThreshold { get; set; } = 0.65d;

    /// <summary>Gets or sets a value indicating whether responses are cached.</summary>
    public bool EnableCaching { get; set; } = true;

    /// <summary>Gets or sets the cache entry lifetime in minutes.</summary>
    [Range(1, 43200)]
    public int CacheTimeToLiveMinutes { get; set; } = 240;

    /// <summary>Gets or sets a value indicating whether providers are probed before first use.</summary>
    public bool ProbeBeforeUse { get; set; } = true;

    /// <summary>Gets the resilience settings applied to every provider.</summary>
    public AiResilienceOptions Resilience { get; } = new();
}
