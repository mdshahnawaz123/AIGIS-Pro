using System.Linq.Expressions;

namespace AiGisConverter.Domain.Specifications;

/// <summary>
/// A named, composable query predicate.
/// </summary>
/// <typeparam name="T">The entity the specification selects.</typeparam>
/// <remarks>
/// Expressed as an expression tree rather than a delegate so the data layer can translate it into
/// SQL. A <c>Func&lt;T, bool&gt;</c> would force every candidate row into memory before filtering,
/// which is exactly the behaviour that makes a run-history screen slow after a few months of use.
/// </remarks>
public interface ISpecification<T>
{
    /// <summary>Gets the predicate.</summary>
    /// <returns>An expression suitable for translation by a query provider.</returns>
    Expression<Func<T, bool>> ToExpression();

    /// <summary>Evaluates the predicate against a single candidate, in memory.</summary>
    /// <param name="candidate">The candidate to test.</param>
    /// <returns><see langword="true"/> when the candidate satisfies the specification.</returns>
    bool IsSatisfiedBy(T candidate);
}
