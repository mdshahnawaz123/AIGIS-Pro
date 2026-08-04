namespace AiGisConverter.Ai.Exceptions;

/// <summary>
/// Raised when an AI provider fails in a way the caller cannot recover from locally.
/// </summary>
public class AIProviderException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="AIProviderException"/> class.</summary>
    public AIProviderException()
        : this("The AI provider failed.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AIProviderException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public AIProviderException(string message)
        : base(message) => ProviderKey = "unknown";

    /// <summary>Initializes a new instance of the <see cref="AIProviderException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public AIProviderException(string message, Exception innerException)
        : base(message, innerException) => ProviderKey = "unknown";

    /// <summary>Initializes a new instance of the <see cref="AIProviderException"/> class.</summary>
    /// <param name="providerKey">Key of the failing provider.</param>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    public AIProviderException(string providerKey, string message, Exception? innerException = null)
        : base(message, innerException) => ProviderKey = providerKey;

    /// <summary>Gets the key of the provider that failed.</summary>
    public string ProviderKey { get; }
}
