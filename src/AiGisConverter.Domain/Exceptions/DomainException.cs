namespace AiGisConverter.Domain.Exceptions;

/// <summary>
/// Base class for failures that represent a broken domain rule.
/// </summary>
/// <remarks>
/// <para>
/// Domain exceptions signal a <em>programming</em> error: an invariant was violated, which means
/// something upstream failed to validate. Expected, recoverable failures &#8212; an unreadable
/// file, an unreachable model endpoint &#8212; are returned as
/// <see cref="Common.Result"/> instead, because they are ordinary outcomes rather than bugs.
/// </para>
/// <para>
/// The distinction matters at the call site: a <see cref="Common.Result"/> asks to be handled,
/// while a <see cref="DomainException"/> asks to be fixed.
/// </para>
/// </remarks>
public class DomainException : Exception
{
    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    public DomainException()
        : base("A domain rule was violated.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    /// <param name="message">Description of the violated rule.</param>
    public DomainException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainException"/> class.</summary>
    /// <param name="message">Description of the violated rule.</param>
    /// <param name="innerException">The underlying failure.</param>
    public DomainException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
