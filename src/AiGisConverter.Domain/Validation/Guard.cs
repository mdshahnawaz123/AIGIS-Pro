using System.Globalization;
using System.Runtime.CompilerServices;
using AiGisConverter.Domain.Exceptions;

namespace AiGisConverter.Domain.Validation;

/// <summary>
/// Guard clauses for domain invariants.
/// </summary>
/// <remarks>
/// <para>
/// These sit alongside the BCL's <c>ArgumentNullException.ThrowIfNull</c> family rather than
/// replacing it: use the BCL helpers for argument contracts, and these for the domain-specific
/// checks the BCL has no opinion about, such as finiteness of a coordinate.
/// </para>
/// <para>
/// Every method captures the caller's expression, so a failure names the offending code rather
/// than an anonymous parameter.
/// </para>
/// </remarks>
public static class Guard
{
    /// <summary>Requires a double to be a real, finite number.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="parameterName">Captured automatically.</param>
    /// <returns>The value, so the guard can be used inline.</returns>
    /// <exception cref="DomainException">The value is NaN or infinite.</exception>
    public static double AgainstNonFinite(
        double value,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new DomainException(string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' must be a finite number but was {1}.",
                parameterName,
                value));
        }

        return value;
    }

    /// <summary>Requires a value to fall within an inclusive range.</summary>
    /// <param name="value">The value to check.</param>
    /// <param name="minimum">Inclusive lower bound.</param>
    /// <param name="maximum">Inclusive upper bound.</param>
    /// <param name="parameterName">Captured automatically.</param>
    /// <returns>The value, so the guard can be used inline.</returns>
    /// <exception cref="DomainException">The value is outside the range.</exception>
    public static double AgainstOutOfRange(
        double value,
        double minimum,
        double maximum,
        [CallerArgumentExpression(nameof(value))] string? parameterName = null)
    {
        if (double.IsNaN(value) || value < minimum || value > maximum)
        {
            throw new DomainException(string.Format(
                CultureInfo.InvariantCulture,
                "'{0}' must be within [{1}, {2}] but was {3}.",
                parameterName,
                minimum,
                maximum,
                value));
        }

        return value;
    }

    /// <summary>Requires a collection to contain at least one item.</summary>
    /// <typeparam name="T">The item type.</typeparam>
    /// <param name="items">The collection to check.</param>
    /// <param name="parameterName">Captured automatically.</param>
    /// <returns>The collection, so the guard can be used inline.</returns>
    /// <exception cref="DomainException">The collection is empty.</exception>
    public static IReadOnlyCollection<T> AgainstEmpty<T>(
        IReadOnlyCollection<T> items,
        [CallerArgumentExpression(nameof(items))] string? parameterName = null)
    {
        ArgumentNullException.ThrowIfNull(items, parameterName);

        if (items.Count == 0)
        {
            throw new DomainException($"'{parameterName}' must contain at least one item.");
        }

        return items;
    }

    /// <summary>Requires a condition to hold.</summary>
    /// <param name="condition">The condition that must be true.</param>
    /// <param name="message">What is wrong when it is not.</param>
    /// <exception cref="DomainException">The condition is false.</exception>
    public static void Requires(bool condition, string message)
    {
        if (!condition)
        {
            throw new DomainException(message);
        }
    }
}
