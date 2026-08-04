using System.Globalization;

namespace AiGisConverter.Ai.Exceptions;

/// <summary>
/// Raised when configuration names a provider that was never registered with the container.
/// </summary>
public sealed class AIProviderNotRegisteredException : AIProviderException
{
    /// <summary>Initializes a new instance of the <see cref="AIProviderNotRegisteredException"/> class.</summary>
    public AIProviderNotRegisteredException()
        : base("The requested AI provider is not registered.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AIProviderNotRegisteredException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public AIProviderNotRegisteredException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AIProviderNotRegisteredException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public AIProviderNotRegisteredException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>
    /// Creates an exception naming the requested key and listing what is actually available,
    /// which is the fastest way to diagnose a configuration typo.
    /// </summary>
    /// <param name="requestedKey">The key that could not be resolved.</param>
    /// <param name="registeredKeys">The keys that are registered.</param>
    /// <returns>A new <see cref="AIProviderNotRegisteredException"/>.</returns>
    public static AIProviderNotRegisteredException For(string requestedKey, IEnumerable<string> registeredKeys) =>
        new(string.Format(
            CultureInfo.InvariantCulture,
            "AI provider '{0}' is not registered. Registered providers: {1}. " +
            "Check 'Ai:ActiveProvider' in appsettings.json and the AddAiLayer(...) registration.",
            requestedKey,
            string.Join(", ", registeredKeys)));
}
