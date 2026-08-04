using System.Diagnostics;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Domain.Entities.Ai;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// Template for every chat-completion provider: chunk the subjects, build one prompt per chunk,
/// send it, parse the answer, aggregate.
/// </summary>
/// <remarks>
/// <para>
/// A concrete chat provider supplies only three things: its <see cref="IAIProvider.Key"/>,
/// its <see cref="IAIProvider.Metadata"/>, and how to put a <see cref="ChatPrompt"/> on the wire
/// (<see cref="CompleteAsync"/>). Everything else &#8212; chunking, prompt text, JSON tolerance,
/// telemetry aggregation &#8212; is shared and tested once.
/// </para>
/// <para>
/// This is the seam that makes Azure OpenAI, Claude, Gemini, Hugging Face Inference and LM Studio
/// roughly fifty lines each: they differ only in endpoint, auth header and response envelope.
/// </para>
/// </remarks>
public abstract class ChatCompletionProviderBase : IAIProvider
{
    private readonly IChatPromptBuilder _promptBuilder;
    private readonly IClassificationResponseParser _responseParser;

    /// <summary>Initializes a new instance of the <see cref="ChatCompletionProviderBase"/> class.</summary>
    /// <param name="promptBuilder">Builds the vendor-neutral prompt.</param>
    /// <param name="responseParser">Parses the model's answer.</param>
    /// <param name="logger">Logger for this provider.</param>
    protected ChatCompletionProviderBase(
        IChatPromptBuilder promptBuilder,
        IClassificationResponseParser responseParser,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(promptBuilder);
        ArgumentNullException.ThrowIfNull(responseParser);
        ArgumentNullException.ThrowIfNull(logger);

        _promptBuilder = promptBuilder;
        _responseParser = responseParser;
        Logger = logger;
    }

    /// <inheritdoc />
    public abstract string Key { get; }

    /// <inheritdoc />
    public abstract AIProviderMetadata Metadata { get; }

    /// <summary>Gets the logger for the concrete provider.</summary>
    protected ILogger Logger { get; }

    /// <inheritdoc />
    public abstract Task<AIProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Sends a prompt to the underlying service and returns the normalised completion.</summary>
    /// <param name="prompt">The prompt to send.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The assistant response, stripped of vendor envelope.</returns>
    protected abstract Task<ChatCompletion> CompleteAsync(ChatPrompt prompt, CancellationToken cancellationToken);

    /// <inheritdoc />
    public async Task<AIClassificationResponse> ClassifyAsync(
        AIClassificationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        long startedAt = Stopwatch.GetTimestamp();
        List<ClassificationResult> aggregated = new(request.Subjects.Count);
        string? modelIdentifier = null;
        int promptTokens = 0;
        int completionTokens = 0;
        bool sawTokenCounts = false;

        foreach (IReadOnlyList<ClassificationSubject> chunk in Chunk(request.Subjects, Metadata.MaxSubjectsPerCall))
        {
            cancellationToken.ThrowIfCancellationRequested();

            ChatPrompt prompt = _promptBuilder.Build(request, chunk);
            ChatCompletion completion;

            try
            {
                completion = await CompleteAsync(prompt, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (AIProviderException)
            {
                throw;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or FormatException)
            {
                throw new AIProviderException(Key, $"Chat completion failed for provider '{Key}'.", ex);
            }

            modelIdentifier ??= completion.ModelIdentifier;

            if (completion.PromptTokens is { } pt)
            {
                promptTokens += pt;
                sawTokenCounts = true;
            }

            if (completion.CompletionTokens is { } ct)
            {
                completionTokens += ct;
                sawTokenCounts = true;
            }

            IReadOnlyList<ClassificationResult> parsed =
                _responseParser.Parse(completion.Content, chunk, request.Context, Key);

            if (parsed.Count < chunk.Count)
            {
                Logger.LogWarning(
                    "Provider {ProviderKey} answered for {AnsweredCount} of {RequestedCount} subjects.",
                    Key,
                    parsed.Count,
                    chunk.Count);
            }

            aggregated.AddRange(parsed);
        }

        AIUsage usage = new(
            sawTokenCounts ? promptTokens : null,
            sawTokenCounts ? completionTokens : null,
            Stopwatch.GetElapsedTime(startedAt));

        return new AIClassificationResponse(aggregated, Key, modelIdentifier, usage);
    }

    /// <summary>Splits subjects into fixed-size chunks.</summary>
    /// <param name="subjects">The subjects to split.</param>
    /// <param name="size">Maximum chunk size.</param>
    /// <returns>The chunks, in order.</returns>
    private static IEnumerable<IReadOnlyList<ClassificationSubject>> Chunk(
        IReadOnlyList<ClassificationSubject> subjects,
        int size)
    {
        int effectiveSize = Math.Max(1, size);

        for (int offset = 0; offset < subjects.Count; offset += effectiveSize)
        {
            int length = Math.Min(effectiveSize, subjects.Count - offset);
            ClassificationSubject[] buffer = new ClassificationSubject[length];

            for (int i = 0; i < length; i++)
            {
                buffer[i] = subjects[offset + i];
            }

            yield return buffer;
        }
    }
}
