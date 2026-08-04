namespace AiGisConverter.Plugins.AiProviders;

/// <summary>
/// One OpenAI-compatible endpoint, configured under
/// <c>Plugins:aigis.ai.providers:endpoints</c>.
/// </summary>
public sealed class OpenAiCompatibleEndpointOptions
{
    /// <summary>Gets or sets the provider key exposed to <c>Ai:ActiveProvider</c>, for example <c>lmstudio</c>.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Gets or sets the name shown in the provider picker.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the base address, which must end in a slash.</summary>
    public string BaseAddress { get; set; } = "http://localhost:1234/v1/";

    /// <summary>Gets or sets the model identifier.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the environment variable holding the API key, when the endpoint needs one.</summary>
    public string? ApiKeyEnvironmentVariable { get; set; }

    /// <summary>Gets or sets the header carrying the key. Azure OpenAI uses <c>api-key</c>.</summary>
    public string AuthenticationHeader { get; set; } = "Authorization";

    /// <summary>Gets or sets the value prefix. Empty for Azure OpenAI's <c>api-key</c> header.</summary>
    public string AuthenticationScheme { get; set; } = "Bearer";

    /// <summary>Gets or sets the sampling temperature.</summary>
    public double Temperature { get; set; }

    /// <summary>Gets or sets the request timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Gets or sets how many subjects are sent per call.</summary>
    public int MaxSubjectsPerCall { get; set; } = 25;

    /// <summary>Gets or sets a value indicating whether to request a guaranteed JSON object.</summary>
    public bool UseJsonResponseFormat { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the endpoint reaches the public internet.</summary>
    public bool RequiresNetwork { get; set; } = true;
}

/// <summary>Root options for the AI Providers plugin.</summary>
public sealed class AiProvidersPluginOptions
{
    /// <summary>Gets the configured endpoints. Each becomes one selectable provider.</summary>
    public IList<OpenAiCompatibleEndpointOptions> Endpoints { get; } = [];
}
