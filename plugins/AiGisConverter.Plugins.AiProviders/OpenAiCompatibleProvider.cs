using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.AiProviders;

/// <summary>
/// A configurable OpenAI-compatible chat provider.
/// </summary>
/// <remarks>
/// <para>
/// Implemented entirely inside a plugin. It derives from the AI layer's
/// <see cref="ChatCompletionProviderBase"/>, so it inherits chunking, prompt construction and
/// tolerant JSON parsing, and supplies only the wire call &#8212; which is the whole point of
/// that base class existing.
/// </para>
/// <para>
/// One class covers LM Studio, vLLM, OpenRouter, Together and Azure OpenAI, because they differ
/// only in base address, model name and which header carries the key.
/// </para>
/// </remarks>
internal sealed class OpenAiCompatibleProvider : ChatCompletionProviderBase, IDisposable
{
    private readonly OpenAiCompatibleEndpointOptions _options;
    private readonly HttpClient _client;

    /// <summary>Initializes a new instance of the <see cref="OpenAiCompatibleProvider"/> class.</summary>
    /// <param name="options">The endpoint configuration.</param>
    /// <param name="promptBuilder">Builds the vendor-neutral prompt.</param>
    /// <param name="responseParser">Parses the model's answer.</param>
    /// <param name="logger">Logger for the provider.</param>
    public OpenAiCompatibleProvider(
        OpenAiCompatibleEndpointOptions options,
        IChatPromptBuilder promptBuilder,
        IClassificationResponseParser responseParser,
        ILogger logger)
        : base(promptBuilder, responseParser, logger)
    {
        ArgumentNullException.ThrowIfNull(options);

        _options = options;
        _client = new HttpClient
        {
            BaseAddress = new Uri(options.BaseAddress.EndsWith('/') ? options.BaseAddress : options.BaseAddress + "/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds),
        };

        ApplyAuthentication();
    }

    /// <inheritdoc />
    public override string Key => _options.Key;

    /// <inheritdoc />
    public override AIProviderMetadata Metadata => new(
        _options.Key,
        string.IsNullOrWhiteSpace(_options.DisplayName) ? _options.Key : _options.DisplayName,
        _options.RequiresNetwork ? AIProviderKind.RemoteService : AIProviderKind.LocalService,
        _options.MaxSubjectsPerCall,
        SupportsRationale: true,
        _options.RequiresNetwork);

    /// <inheritdoc />
    public override async Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using HttpResponseMessage response = await _client
                .GetAsync(new Uri("models", UriKind.Relative), cancellationToken)
                .ConfigureAwait(false);

            return response.StatusCode switch
            {
                HttpStatusCode.OK => AIProviderAvailability.Available(_options.Model),
                HttpStatusCode.Unauthorized => AIProviderAvailability.Unavailable("The API key was rejected."),
                _ => AIProviderAvailability.Unavailable(
                    $"{_options.BaseAddress} returned {(int)response.StatusCode} {response.ReasonPhrase}."),
            };
        }
        catch (HttpRequestException ex)
        {
            return AIProviderAvailability.Unavailable($"{_options.BaseAddress} is unreachable: {ex.Message}");
        }
        catch (TaskCanceledException)
        {
            return AIProviderAvailability.Unavailable($"{_options.BaseAddress} did not respond in time.");
        }
    }

    /// <inheritdoc />
    protected override async Task<ChatCompletion> CompleteAsync(
        ChatPrompt prompt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);

        ChatRequest payload = new()
        {
            Model = _options.Model,
            Temperature = _options.Temperature,
            ResponseFormat = _options.UseJsonResponseFormat ? new ResponseFormat() : null,
            Messages =
            [
                new Message { Role = "system", Content = prompt.SystemMessage },
                new Message { Role = "user", Content = prompt.UserMessage },
            ],
        };

        try
        {
            using HttpResponseMessage response = await _client
                .PostAsJsonAsync("chat/completions", payload, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                throw new AIProviderException(
                    Key,
                    $"{_options.BaseAddress} returned {(int)response.StatusCode} {response.ReasonPhrase}. " +
                    (body.Length <= 400 ? body : body[..400]));
            }

            ChatResponse? parsed = await response.Content
                .ReadFromJsonAsync<ChatResponse>(cancellationToken)
                .ConfigureAwait(false);

            Message? message = parsed?.Choices.Count > 0 ? parsed.Choices[0].Message : null;

            return message is null
                ? throw new AIProviderException(Key, $"{_options.BaseAddress} returned no choices.")
                : new ChatCompletion(
                    message.Content,
                    parsed!.Model,
                    parsed.Usage?.PromptTokens,
                    parsed.Usage?.CompletionTokens);
        }
        catch (HttpRequestException ex)
        {
            throw new AIProviderException(Key, $"{_options.BaseAddress} is unreachable.", ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new AIProviderException(Key, $"The request exceeded {_options.TimeoutSeconds}s.", ex);
        }
        catch (JsonException ex)
        {
            throw new AIProviderException(Key, "The endpoint returned an unreadable payload.", ex);
        }
    }

    /// <inheritdoc />
    public void Dispose() => _client.Dispose();

    private void ApplyAuthentication()
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKeyEnvironmentVariable))
        {
            return;
        }

        string? key = Environment.GetEnvironmentVariable(_options.ApiKeyEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(key))
        {
            return;
        }

        if (_options.AuthenticationHeader.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
        {
            _client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(_options.AuthenticationScheme, key);
        }
        else
        {
            _client.DefaultRequestHeaders.Add(_options.AuthenticationHeader, key);
        }
    }

    private sealed record ChatRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; init; } = string.Empty;

        [JsonPropertyName("messages")]
        public IReadOnlyList<Message> Messages { get; init; } = [];

        [JsonPropertyName("temperature")]
        public double Temperature { get; init; }

        [JsonPropertyName("response_format")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public ResponseFormat? ResponseFormat { get; init; }
    }

    private sealed record Message
    {
        [JsonPropertyName("role")]
        public string Role { get; init; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; init; } = string.Empty;
    }

    private sealed record ResponseFormat
    {
        [JsonPropertyName("type")]
        public string Type { get; init; } = "json_object";
    }

    private sealed record ChatResponse
    {
        [JsonPropertyName("model")]
        public string? Model { get; init; }

        [JsonPropertyName("choices")]
        public IReadOnlyList<Choice> Choices { get; init; } = [];

        [JsonPropertyName("usage")]
        public Usage? Usage { get; init; }
    }

    private sealed record Choice
    {
        [JsonPropertyName("message")]
        public Message? Message { get; init; }
    }

    private sealed record Usage
    {
        [JsonPropertyName("prompt_tokens")]
        public int? PromptTokens { get; init; }

        [JsonPropertyName("completion_tokens")]
        public int? CompletionTokens { get; init; }
    }
}
