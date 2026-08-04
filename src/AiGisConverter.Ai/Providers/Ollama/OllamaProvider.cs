using System.Net.Http.Json;
using System.Text.Json;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Providers.Ollama;

/// <summary>
/// Classifies using a local Ollama server over its native <c>/api/chat</c> endpoint.
/// </summary>
/// <remarks>
/// Everything Ollama-specific is contained here: the endpoint path, the <c>keep_alive</c> and
/// <c>format</c> parameters, and the <c>prompt_eval_count</c>/<c>eval_count</c> token fields.
/// Chunking, prompt text and response parsing come from <see cref="ChatCompletionProviderBase"/>.
/// </remarks>
public sealed class OllamaProvider : ChatCompletionProviderBase
{
    /// <summary>The configuration key and provider key for this provider.</summary>
    public const string ProviderKey = "ollama";

    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used by this provider.</summary>
    public const string HttpClientName = "ai.ollama";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OllamaOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="OllamaProvider"/> class.</summary>
    /// <param name="httpClientFactory">Factory supplying the configured HTTP client.</param>
    /// <param name="options">Live provider options.</param>
    /// <param name="promptBuilder">Builds the vendor-neutral prompt.</param>
    /// <param name="responseParser">Parses the model's answer.</param>
    /// <param name="logger">Logger for the provider.</param>
    public OllamaProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OllamaOptions> options,
        IChatPromptBuilder promptBuilder,
        IClassificationResponseParser responseParser,
        ILogger<OllamaProvider> logger)
        : base(promptBuilder, responseParser, logger)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(options);

        _httpClientFactory = httpClientFactory;
        _options = options;
    }

    /// <inheritdoc />
    public override string Key => ProviderKey;

    /// <inheritdoc />
    public override AIProviderMetadata Metadata => new(
        ProviderKey,
        "Ollama (local LLM)",
        AIProviderKind.LocalService,
        _options.CurrentValue.MaxSubjectsPerCall,
        SupportsRationale: true,
        RequiresNetwork: false);

    /// <inheritdoc />
    public override async Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        OllamaOptions options = _options.CurrentValue;

        try
        {
            using HttpClient client = CreateClient(options);
            OllamaTagsResponse? tags = await client
                .GetFromJsonAsync<OllamaTagsResponse>("api/tags", cancellationToken)
                .ConfigureAwait(false);

            if (tags is null || tags.Models.Count == 0)
            {
                return AIProviderAvailability.Unavailable(
                    $"Ollama at {options.Endpoint} has no models installed. Run 'ollama pull {options.Model}'.");
            }

            bool installed = tags.Models.Any(m =>
                m.Name.Equals(options.Model, StringComparison.OrdinalIgnoreCase) ||
                m.Name.StartsWith(options.Model + ":", StringComparison.OrdinalIgnoreCase));

            return installed
                ? AIProviderAvailability.Available(options.Model)
                : AIProviderAvailability.Unavailable(
                    $"Model '{options.Model}' is not installed. Run 'ollama pull {options.Model}'.");
        }
        catch (HttpRequestException ex)
        {
            return AIProviderAvailability.Unavailable($"Ollama at {options.Endpoint} is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return AIProviderAvailability.Unavailable($"Ollama at {options.Endpoint} did not respond in time.");
        }
        catch (JsonException ex)
        {
            return AIProviderAvailability.Unavailable($"Ollama returned an unexpected payload: {ex.Message}");
        }
    }

    /// <inheritdoc />
    protected override async Task<ChatCompletion> CompleteAsync(
        ChatPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        OllamaOptions options = _options.CurrentValue;

        OllamaChatRequest payload = new()
        {
            Model = options.Model,
            Stream = false,
            Format = options.UseJsonFormat ? "json" : null,
            KeepAlive = string.IsNullOrWhiteSpace(options.KeepAlive) ? null : options.KeepAlive,
            Options = new OllamaRequestOptions { Temperature = options.Temperature },
            Messages =
            [
                new OllamaMessage { Role = "system", Content = prompt.SystemMessage },
                new OllamaMessage { Role = "user", Content = prompt.UserMessage },
            ],
        };

        try
        {
            using HttpClient client = CreateClient(options);
            using HttpResponseMessage response = await client
                .PostAsJsonAsync("api/chat", payload, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new AIProviderException(
                    Key,
                    $"Ollama returned {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(body)}");
            }

            OllamaChatResponse? parsed = await response.Content
                .ReadFromJsonAsync<OllamaChatResponse>(cancellationToken)
                .ConfigureAwait(false);

            if (parsed?.Message is null)
            {
                throw new AIProviderException(Key, "Ollama returned a response without a message.");
            }

            return new ChatCompletion(
                parsed.Message.Content,
                parsed.Model,
                parsed.PromptEvalCount,
                parsed.EvalCount);
        }
        catch (HttpRequestException ex)
        {
            throw new AIProviderException(Key, $"Ollama at {options.Endpoint} is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AIProviderException(Key, $"Ollama did not respond within {options.TimeoutSeconds}s.", ex);
        }
        catch (JsonException ex)
        {
            throw new AIProviderException(Key, "Ollama returned an unreadable payload.", ex);
        }
    }

    private HttpClient CreateClient(OllamaOptions options)
    {
        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = NormaliseBaseAddress(options.Endpoint);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);

        return client;
    }

    /// <summary>Ensures the base address ends with a slash so relative paths append correctly.</summary>
    private static Uri NormaliseBaseAddress(Uri? endpoint)
    {
        Uri resolved = endpoint ?? new Uri("http://localhost:11434");

        return resolved.AbsoluteUri.EndsWith('/') ? resolved : new Uri(resolved.AbsoluteUri + "/");
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
