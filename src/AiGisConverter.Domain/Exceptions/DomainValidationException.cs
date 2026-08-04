using AiGisConverter.Domain.Validation;

namespace AiGisConverter.Domain.Exceptions;

/// <summary>
/// Raised when an entity would be left in an invalid state.
/// </summary>
public sealed class DomainValidationException : DomainException
{
    /// <summary>Initializes a new instance of the <see cref="DomainValidationException"/> class.</summary>
    public DomainValidationException()
        : this("The entity failed validation.")
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DomainValidationException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    public DomainValidationException(string message)
        : base(message) => Failures = [];

    /// <summary>Initializes a new instance of the <see cref="DomainValidationException"/> class.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <param name="innerException">The underlying failure.</param>
    public DomainValidationException(string message, Exception innerException)
        : base(message, innerException) => Failures = [];

    /// <summary>Initializes a new instance of the <see cref="DomainValidationException"/> class.</summary>
    /// <param name="failures">The individual failures.</param>
    public DomainValidationException(IReadOnlyList<ValidationFailure> failures)
        : base(BuildMessage(failures)) => Failures = failures;

    /// <summary>Gets the individual failures.</summary>
    public IReadOnlyList<ValidationFailure> Failures { get; }

    private static string BuildMessage(IReadOnlyList<ValidationFailure> failures)
    {
        ArgumentNullException.ThrowIfNull(failures);

        return failures.Count switch
        {
            0 => "The entity failed validation.",
            1 => failures[0].ToString(),
            _ => $"The entity failed validation with {failures.Count} problems: " +
                 string.Join("; ", failures.Select(static f => f.ToString())),
        };
    }
}
