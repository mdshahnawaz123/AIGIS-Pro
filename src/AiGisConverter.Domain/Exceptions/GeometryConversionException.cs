namespace AiGisConverter.Domain.Exceptions;

/// <summary>
/// Raised when a source primitive cannot be expressed as valid GIS geometry.
/// </summary>
public sealed class GeometryConversionException : DomainException
{
    /// <summary>Initializes a new instance of the <see cref="GeometryConversionException"/> class.</summary>
    public GeometryConversionException()
        : this("The geometry could not be converted.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="GeometryConversionException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public GeometryConversionException(string message)
        : base(message) => ElementId = null;

    /// <summary>Initializes a new instance of the <see cref="GeometryConversionException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public GeometryConversionException(string message, Exception innerException)
        : base(message, innerException) => ElementId = null;

    /// <summary>Initializes a new instance of the <see cref="GeometryConversionException"/> class.</summary>
    /// <param name="elementId">The offending source element.</param>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure, when there is one.</param>
    public GeometryConversionException(string elementId, string message, Exception? innerException = null)
        : base(message, innerException!) => ElementId = elementId;

    /// <summary>Gets the identifier of the offending source element, when known.</summary>
    public string? ElementId { get; }
}
