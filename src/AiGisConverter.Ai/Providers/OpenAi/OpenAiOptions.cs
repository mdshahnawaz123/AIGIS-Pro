using AiGisConverter.Ai.Options;

namespace AiGisConverter.Ai.Providers.OpenAi;

/// <summary>
/// Options for <see cref="OpenAiProvider"/>, bound from <c>Ai:Providers:openai</c>.
/// </summary>
public sealed class OpenAiOptions : ChatProviderOptions
{
    /// <summary>Initializes a new instance of the <see cref="OpenAiOptions"/> class.</summary>
    public OpenAiOptions()
    {
        Endpoint = new Uri("https://api.openai.com/v1/");
        Model = "gpt-4o-mini";
        MaxSubjectsPerCall = 25;
    }

    /// <summary>
    /// Gets or sets the name of the environment variable holding the API key.
    /// </summary>
    /// <remarks>
    /// The key itself is deliberately not a configuration value, so it is never written to
    /// <c>appsettings.json</c>, never committed, and never captured in a support bundle.
    /// </remarks>
    public string ApiKeyEnvironmentVariable { get; set; } = "OPENAI_API_KEY";

    /// <summary>Gets or sets the optional organisation identifier sent as <c>OpenAI-Organization</c>.</summary>
    public string? Organization { get; set; }

    /// <summary>Gets or sets the optional project identifier sent as <c>OpenAI-Project</c>.</summary>
    public string? Project { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the request asks for a guaranteed JSON object.
    /// Disable for models or gateways that do not implement <c>response_format</c>.
    /// </summary>
    public bool UseJsonResponseFormat { get; set; } = true;
}
