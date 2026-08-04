using AiGisConverter.Domain.Exceptions;

namespace AiGisConverter.Domain.Validation;

/// <summary>
/// The accumulated result of validating an object: every problem at once, not just the first.
/// </summary>
/// <remarks>
/// Reporting all failures together matters for a desktop tool. A user correcting export settings
/// should not have to submit the form six times to discover six problems.
/// </remarks>
public sealed class ValidationOutcome
{
    private readonly List<ValidationFailure> _failures = [];

    /// <summary>Gets a value indicating whether no failures were recorded.</summary>
    public bool IsValid => _failures.Count == 0;

    /// <summary>Gets the recorded failures.</summary>
    public IReadOnlyList<ValidationFailure> Failures => _failures;

    /// <summary>Records a failure.</summary>
    /// <param name="memberName">The property or argument at fault.</param>
    /// <param name="message">What is wrong with it.</param>
    /// <param name="code">Stable machine-readable code.</param>
    /// <returns>The same outcome, for chaining.</returns>
    public ValidationOutcome Add(string memberName, string message, string? code = null)
    {
        _failures.Add(new ValidationFailure(memberName, message, code));
        return this;
    }

    /// <summary>Records a failure only when a condition holds.</summary>
    /// <param name="condition">When <see langword="true"/>, the failure is recorded.</param>
    /// <param name="memberName">The property or argument at fault.</param>
    /// <param name="message">What is wrong with it.</param>
    /// <param name="code">Stable machine-readable code.</param>
    /// <returns>The same outcome, for chaining.</returns>
    public ValidationOutcome AddIf(bool condition, string memberName, string message, string? code = null) =>
        condition ? Add(memberName, message, code) : this;

    /// <summary>Throws when any failure was recorded.</summary>
    /// <exception cref="DomainValidationException">At least one failure was recorded.</exception>
    public void ThrowIfInvalid()
    {
        if (!IsValid)
        {
            throw new DomainValidationException(Failures);
        }
    }
}
