namespace AiGisConverter.Ai.Abstractions;

/// <summary>
/// A cross-cutting concern applied uniformly to every registered provider.
/// </summary>
/// <remarks>
/// Logging, resilience and caching are implemented as decorators rather than as base-class
/// behaviour, so a new provider inherits them for free and a new concern can be added without
/// touching any provider. Decorators are applied in ascending <see cref="Order"/>; the lowest
/// order ends up outermost.
/// </remarks>
public interface IAIProviderDecorator
{
    /// <summary>Gets the application order. Lower values wrap higher values.</summary>
    int Order { get; }

    /// <summary>Wraps a provider.</summary>
    /// <param name="inner">The provider to wrap.</param>
    /// <returns>The wrapped provider.</returns>
    IAIProvider Decorate(IAIProvider inner);
}
