using System.Globalization;

namespace AiGisConverter.Domain.Exceptions;

/// <summary>
/// Raised when an entity that the caller has asserted must exist does not.
/// </summary>
/// <remarks>
/// Repositories return <see langword="null"/> for an ordinary miss. This exception is for the case
/// where absence is a contradiction &#8212; a run referencing a job that is not there &#8212;
/// which is a data-integrity problem, not a lookup result.
/// </remarks>
public sealed class EntityNotFoundException : DomainException
{
    /// <summary>Initializes a new instance of the <see cref="EntityNotFoundException"/> class.</summary>
    public EntityNotFoundException()
        : this("The entity was not found.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EntityNotFoundException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public EntityNotFoundException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="EntityNotFoundException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public EntityNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception naming the entity type and identifier.</summary>
    /// <param name="entityName">The entity type name.</param>
    /// <param name="id">The identifier that was not found.</param>
    /// <returns>A new <see cref="EntityNotFoundException"/>.</returns>
    public static EntityNotFoundException For(string entityName, object id) =>
        new(string.Format(CultureInfo.InvariantCulture, "{0} '{1}' was not found.", entityName, id));
}
