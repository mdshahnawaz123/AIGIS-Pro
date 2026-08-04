# AI layer

`src/AiGisConverter.Ai` — pluggable AI classification behind one contract.

## Pipeline

```
Application
    │  IAiClassifier                     (port, declared in Domain)
    ▼
AiClassificationService                  policy: threshold, fallback, one-result-per-subject
    │  IAIProviderFactory
    ▼
LoggingAIProviderDecorator        order 0   (outermost)
    └── ResilienceAIProviderDecorator   order 10   timeout + exponential backoff
            └── CachingAIProviderDecorator  order 20
                    └── IAIProvider          ← the only extension point
                            ├── RuleBasedProvider   deterministic, offline
                            ├── OnnxProvider        local model, tensors
                            ├── OllamaProvider      ┐ derive from
                            └── OpenAiProvider      ┘ ChatCompletionProviderBase
```

## Configuration

```jsonc
"Ai": {
  "ActiveProvider": "ollama",        // empty => factory picks the least demanding registered provider
  "FallbackProvider": "rulebased",   // "none" disables fallback
  "ConfidenceThreshold": 0.65,
  "EnableCaching": true,
  "Resilience": { "MaxRetries": 2, "BaseDelayMilliseconds": 500, "PerAttemptTimeoutSeconds": 120 },
  "Providers": {
    "rulebased": { "KeywordRules": { "strm": "Stormwater Pipe" } },
    "ollama":    { "Endpoint": "http://localhost:11434", "Model": "llama3.1" },
    "openai":    { "Model": "gpt-4o-mini", "ApiKeyEnvironmentVariable": "OPENAI_API_KEY" },
    "onnx":      { "ModelPath": "assets/models/onnx/layer-classifier.onnx", "FeatureDimension": 512 }
  }
}
```

Each provider binds only its own sub-section. `AiOptions` contains no provider-specific setting.
API keys are read from environment variables, never from configuration files.

## Registration

```csharp
services.AddAiLayer(configuration, providers => providers
    .AddRuleBasedProvider()
    .AddOnnxProvider()
    .AddOllamaProvider()
    .AddOpenAiProvider());
```

## Adding a provider without modifying existing code

A chat vendor needs one class and one extension method. Claude, in full:

```csharp
// Providers/Claude/ClaudeOptions.cs
public sealed class ClaudeOptions : ChatProviderOptions
{
    public ClaudeOptions()
    {
        Endpoint = new Uri("https://api.anthropic.com/v1/");
        Model = "claude-sonnet-5";
    }

    public string ApiKeyEnvironmentVariable { get; set; } = "ANTHROPIC_API_KEY";
    public string AnthropicVersion { get; set; } = "2023-06-01";
}

// Providers/Claude/ClaudeProvider.cs
public sealed class ClaudeProvider : ChatCompletionProviderBase
{
    public const string ProviderKey = "claude";
    public const string HttpClientName = "ai.claude";

    // ctor omitted: same five dependencies as OllamaProvider

    public override string Key => ProviderKey;

    public override AIProviderMetadata Metadata => new(
        ProviderKey, "Claude", AIProviderKind.RemoteService,
        _options.CurrentValue.MaxSubjectsPerCall,
        SupportsRationale: true, RequiresNetwork: true);

    public override Task<AIProviderAvailability> ProbeAsync(CancellationToken ct) => /* key present? */;

    protected override async Task<ChatCompletion> CompleteAsync(ChatPrompt prompt, CancellationToken ct)
    {
        // POST /messages with x-api-key + anthropic-version headers.
        // system => "system", user => messages[0]. Read content[0].text and usage.
    }
}

// Providers/Claude/ClaudeProviderBuilderExtensions.cs
public static IAIProviderBuilder AddClaudeProvider(
    this IAIProviderBuilder builder, Action<ClaudeOptions>? configure = null)
{
    builder.Services.AddHttpClient(ClaudeProvider.HttpClientName);
    return builder.AddProvider<ClaudeProvider, ClaudeOptions>(ClaudeProvider.ProviderKey, configure);
}
```

Then one line at the composition root and one config section. Nothing else changes:

```csharp
providers.AddClaudeProvider();
```

```jsonc
"Ai": { "ActiveProvider": "claude", "Providers": { "claude": { "Model": "claude-sonnet-5" } } }
```

Azure OpenAI is smaller still — derive from `OpenAiProvider` and override `ApplyAuthentication`
to send the `api-key` header instead of `Authorization: Bearer`.

A non-chat engine (a Hugging Face pipeline, a scikit model behind a socket) implements
`IAIProvider` directly, exactly as `OnnxProvider` does.

## Invariants enforced

| Invariant | Where |
|-----------|-------|
| No provider key literal outside `Providers/` | build check |
| Duplicate provider keys fail fast | `AIProviderFactory` constructor |
| Unknown configured key names the registered alternatives | `AIProviderNotRegisteredException.For` |
| Every subject receives a result, even if the model skipped it | `AiClassificationService.Reconcile` |
| A model may only return a label from the candidate set | `JsonClassificationResponseParser` |
| Missing ONNX model does not prevent startup | `OnnxSessionProvider` fails soft |
