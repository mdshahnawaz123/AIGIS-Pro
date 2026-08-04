# ADR 0001 — Pluggable AI provider architecture

- **Status:** Accepted
- **Date:** 2026-07-29
- **Layer:** `AiGisConverter.Ai`

## Context

The AI layer must classify CAD layers into GIS feature classes using any of several engines:
a local ONNX model, a local Ollama server, and the OpenAI API today; Azure OpenAI, Claude,
Gemini, Hugging Face and LM Studio later. The active engine is chosen at runtime from
`appsettings.json`. Adding an engine must not require editing existing code.

Two shapes were considered for the provider contract.

### Option A — prompt-shaped contract

```csharp
Task<string> CompleteAsync(string prompt, CancellationToken ct);
```

Rejected. A tensor-based provider such as ONNX has no notion of a prompt, so it would have to
reverse-engineer one, and the caller would have to build prompts — pushing engine-specific
knowledge outward, which is exactly what the requirement forbids. It also makes the abstraction
leak the assumption "the engine is a language model".

### Option B — task-shaped contract (chosen)

```csharp
Task<AIClassificationResponse> ClassifyAsync(AIClassificationRequest request, CancellationToken ct);
```

The request describes *what to classify* (subjects, candidate labels, domain hint), never *how to
ask*. Each provider translates the task into whatever its engine needs, internally.

## Decision

1. `IAIProvider` is the single extension point, expressed as a classification task.
2. Every provider declares its own `Key`. The core contains no enum of vendors and no `switch`.
3. `AIProviderFactory` indexes `IEnumerable<IAIProvider>` by key and selects one by configuration
   string. It has no compile-time knowledge of any provider.
4. Each provider ships exactly one `IAIProviderBuilder` extension method, which is its only
   contact with the composition root, and binds its own options from
   `Ai:Providers:<key>`. Core options (`AiOptions`) never widen when a provider is added.
5. Chat-based providers derive from `ChatCompletionProviderBase`, which owns chunking, prompt
   construction and tolerant JSON parsing. A new chat vendor implements only `CompleteAsync`.
6. Cross-cutting concerns are `IAIProviderDecorator` implementations applied uniformly to every
   registered provider: logging, resilience, caching. A new provider inherits them for free; a new
   concern is added without touching any provider.
7. `AiClassificationService` adapts the pipeline to the domain port `IAiClassifier` and is the only
   place that holds policy: confidence threshold, fallback, and the guarantee of one result per
   subject.

## Consequences

**Positive**

- Adding a provider is additive: one class plus one extension method. Verified by a build check
  that no provider key literal appears outside `Providers/`.
- The application runs with no model, no endpoint and no network, because `RuleBasedProvider`
  implements the same contract deterministically and acts as the default fallback.
- Providers are unit-testable in isolation; the base class is tested once for all chat vendors.
- One shipped ONNX model can serve several project label schemas, because scores are restricted
  to the intersection of the model's label space and the caller's candidate set, then renormalised.

**Negative**

- The task-shaped contract is less convenient for a caller that genuinely wants free-form
  generation. Should that requirement appear, it belongs on a second, separate port rather than by
  widening `IAIProvider`.
- `ChatCompletionProviderBase` is inheritance, not composition. Justified because the variation
  point is genuinely single (the wire call) and the invariants (chunking, parsing) must not be
  overridden per vendor.

## Verification

- No provider key literal outside `Providers/` (checked in build).
- Exactly one `public const string ProviderKey` per provider.
- Exactly one `IAIProviderBuilder` extension method per provider.
- Duplicate keys fail fast at container construction with a named exception.
