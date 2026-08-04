using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Ai.Providers.OpenAi;

/// <summary>
/// Classifies using the OpenAI chat completions API.
/// </summary>
/// <remarks>
/// Because the endpoint is configurable and the wire format is the de facto standard, this class
/// also works unchanged against Azure OpenAI-compatible gateways, LM Studio, vLLM, Together and
/// OpenRouter. Where a vendor differs only in authentication, deriving from this class and
/// overriding <see cref="ApplyAuthentication"/> is enough.
/// </remarks>
public class OpenAiProvider : ChatCompletionProviderBase
{
    /// <summary>The configuration key and provider key for this provider.</summary>
    public const string ProviderKey = "openai";

    /// <summary>Name of the <see cref="IHttpClientFactory"/> client used by this provider.</summary>
    public const string HttpClientName = "ai.openai";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<OpenAiOptions> _options;

    /// <summary>Initializes a new instance of the <see cref="OpenAiProvider"/> class.</summary>
    /// <param name="httpClientFactory">Factory supplying the configured HTTP client.</param>
    /// <param name="options">Live provider options.</param>
    /// <param name="promptBuilder">Builds the vendor-neutral prompt.</param>
    /// <param name="responseParser">Parses the model's answer.</param>
    /// <param name="logger">Logger for the provider.</param>
    public OpenAiProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<OpenAiOptions> options,
        IChatPromptBuilder promptBuilder,
        IClassificationResponseParser responseParser,
        ILogger<OpenAiProvider> logger)
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
        "OpenAI",
        AIProviderKind.RemoteService,
        _options.CurrentValue.MaxSubjectsPerCall,
        SupportsRationale: true,
        RequiresNetwork: true);

    /// <summary>Gets the current provider options.</summary>
    protected OpenAiOptions CurrentOptions => _options.CurrentValue;

    /// <inheritdoc />
    public override async Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        OpenAiOptions options = _options.CurrentValue;

        if (ResolveApiKey(options) is null)
        {
            return AIProviderAvailability.Unavailable(
                $"No API key. Set the '{options.ApiKeyEnvironmentVariable}' environment variable.");
        }

        try
        {
            using HttpClient client = CreateClient(options);
            using HttpResponseMessage response = await client
                .GetAsync(new Uri("models", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => AIProviderAvailability.Available(options.Model),
                HttpStatusCode.Unauthorized => AIProviderAvailability.Unavailable("The API key was rejected."),
                HttpStatusCode.TooManyRequests => AIProviderAvailability.Unavailable("The account is rate limited."),
                _ => AIProviderAvailability.Unavailable(
                    $"The endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}."),
            };
        }
        catch (HttpRequestException ex)
        {
            return AIProviderAvailability.Unavailable($"{options.Endpoint} is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return AIProviderAvailability.Unavailable($"{options.Endpoint} did not respond in time.");
        }
    }

    /// <inheritdoc />
    protected override async Task<ChatCompletion> CompleteAsync(
        ChatPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        OpenAiOptions options = _options.CurrentValue;

        OpenAiChatRequest payload = new()
        {
            Model = options.Model,
            Temperature = options.Temperature,
            ResponseFormat = options.UseJsonResponseFormat ? new OpenAiResponseFormat() : null,
            Messages =
            [
                new OpenAiMessage { Role = "system", Content = prompt.SystemMessage },
                new OpenAiMessage { Role = "user", Content = prompt.UserMessage },
            ],
        };

        try
        {
            using HttpClient client = CreateClient(options);
            using HttpResponseMessage response = await client
                .PostAsJsonAsync("chat/completions", payload, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new AIProviderException(
                    Key,
                    $"The chat completions endpoint returned {(int)response.StatusCode} " +
                    $"{response.ReasonPhrase}. {Truncate(body)}");
            }

            OpenAiChatResponse? parsed = await response.Content
                .ReadFromJsonAsync<OpenAiChatResponse>(cancellationToken)
                .ConfigureAwait(false);

            OpenAiMessage? message = parsed?.Choices.Count > 0 ? parsed.Choices[0].Message : null;

            if (message is null)
            {
                throw new AIProviderException(Key, "The chat completions endpoint returned no choices.");
            }

            return new ChatCompletion(
                message.Content,
                parsed!.Model,
                parsed.Usage?.PromptTokens,
                parsed.Usage?.CompletionTokens);
        }
        catch (HttpRequestException ex)
        {
            throw new AIProviderException(Key, $"{options.Endpoint} is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AIProviderException(Key, $"The request exceeded {options.TimeoutSeconds}s.", ex);
        }
        catch (JsonException ex)
        {
            throw new AIProviderException(Key, "The endpoint returned an unreadable payload.", ex);
        }
    }

    /// <summary>
    /// Applies authentication headers. Override in a derived provider whose gateway authenticates
    /// differently, for example Azure OpenAI's <c>api-key</c> header.
    /// </summary>
    /// <param name="client">The client to configure.</param>
    /// <param name="options">The current options.</param>
    protected virtual void ApplyAuthentication(HttpClient client, OpenAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(options);

        string? apiKey = ResolveApiKey(options);

        if (apiKey is not null)
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        if (!string.IsNullOrWhiteSpace(options.Organization))
        {
            client.DefaultRequestHeaders.Add("OpenAI-Organization", options.Organization);
        }

        if (!string.IsNullOrWhiteSpace(options.Project))
        {
            client.DefaultRequestHeaders.Add("OpenAI-Project", options.Project);
        }
    }

    /// <summary>Reads the API key from the configured environment variable.</summary>
    /// <param name="options">The current options.</param>
    /// <returns>The key, or <see langword="null"/> when it is not set.</returns>
    protected static string? ResolveApiKey(OpenAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        string? key = Environment.GetEnvironmentVariable(options.ApiKeyEnvironmentVariable);

        return string.IsNullOrWhiteSpace(key) ? null : key;
    }

    /// <summary>Creates and configures the HTTP client for a call.</summary>
    /// <param name="options">The current options.</param>
    /// <returns>A configured client.</returns>
    protected HttpClient CreateClient(OpenAiOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        HttpClient client = _httpClientFactory.CreateClient(HttpClientName);
        client.BaseAddress = NormaliseBaseAddress(options.Endpoint);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        ApplyAuthentication(client, options);

        return client;
    }

    private static Uri NormaliseBaseAddress(Uri? endpoint)
    {
        Uri resolved = endpoint ?? new Uri("https://api.openai.com/v1/");

        return resolved.AbsoluteUri.EndsWith('/') ? resolved : new Uri(resolved.AbsoluteUri + "/");
    }

    private static string Truncate(string value) => value.Length <= 500 ? value : value[..500];
}
