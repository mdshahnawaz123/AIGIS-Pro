namespace AiGisConverter.Domain.Exceptions;

/// <summary>
/// Raised when a coordinate reference system is malformed or cannot be used as requested.
/// </summary>
public sealed class InvalidCoordinateSystemException : DomainException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidCoordinateSystemException"/> class.</summary>
    public InvalidCoordinateSystemException()
        : this("The coordinate reference system is not valid.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidCoordinateSystemException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public InvalidCoordinateSystemException(string message)
        : base(message) => Identifier = null;

    /// <summary>Initializes a new instance of the <see cref="InvalidCoordinateSystemException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public InvalidCoordinateSystemException(string message, Exception innerException)
        : base(message, innerException) => Identifier = null;

    /// <summary>Initializes a new instance of the <see cref="InvalidCoordinateSystemException"/> class.</summary>
    /// <param name="identifier">The offending identifier, for example <c>EPSG:999999</c>.</param>
    /// <param name="message">Description of the failure.</param>
    public InvalidCoordinateSystemException(string identifier, string message)
        : base(message) => Identifier = identifier;

    /// <summary>Gets the offending identifier, when one was supplied.</summary>
    public string? Identifier { get; }
}
