using System.Globalization;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.Exceptions;

/// <summary>
/// Raised when a conversion job or run is asked to make a transition its current state forbids,
/// for example completing a run that was never started.
/// </summary>
public sealed class InvalidConversionStateException : DomainException
{
    /// <summary>Initializes a new instance of the <see cref="InvalidConversionStateException"/> class.</summary>
    public InvalidConversionStateException()
        : this("The conversion is not in a state that allows this operation.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidConversionStateException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public InvalidConversionStateException(string message)
        : base(message)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="InvalidConversionStateException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public InvalidConversionStateException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    /// <summary>Creates an exception describing a forbidden transition.</summary>
    /// <param name="current">The current state.</param>
    /// <param name="attempted">The operation that was attempted.</param>
    /// <returns>A new <see cref="InvalidConversionStateException"/>.</returns>
    public static InvalidConversionStateException For(ConversionStatus current, string attempted) =>
        new(string.Format(
            CultureInfo.InvariantCulture,
            "Cannot {0} while the conversion is {1}.",
            attempted,
            current));
}
