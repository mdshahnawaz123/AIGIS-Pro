using System.ComponentModel.DataAnnotations;

namespace AiGisConverter.Ai.Options;

/// <summary>
/// Settings common to every chat-completion provider. Concrete providers derive from this and
/// add only what is genuinely theirs, such as an API key variable or a keep-alive window.
/// </summary>
public abstract class ChatProviderOptions
{
    /// <summary>Gets or sets the model identifier to request.</summary>
    [Required]
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the service endpoint.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Gets or sets the sampling temperature. Classification wants this near zero.</summary>
    [Range(0d, 2d)]
    public double Temperature { get; set; }

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    [Range(1, 3600)]
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// Gets or sets how many subjects are sent per call. Large drawings are chunked to stay
    /// inside the model's context window and to keep responses parseable.
    /// </summary>
    [Range(1, 500)]
    public int MaxSubjectsPerCall { get; set; } = 25;
}
