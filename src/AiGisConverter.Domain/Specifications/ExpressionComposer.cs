using System.Linq.Expressions;

namespace AiGisConverter.Domain.Specifications;

/// <summary>
/// Joins two lambda expressions into one that a query provider can still translate.
/// </summary>
/// <remarks>
/// The naive approach &#8212; <c>Expression.AndAlso(left.Body, right.Body)</c> &#8212; produces an
/// expression referencing two different parameter instances. It compiles, and it throws at
/// runtime, because a lambda may only bind parameters it declares. Rewriting the right-hand body
/// onto the left's parameter is what makes composed specifications usable in a database query.
/// </remarks>
internal static class ExpressionComposer
{
    /// <summary>Combines two predicates with a binary operator.</summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <param name="left">The left predicate.</param>
    /// <param name="right">The right predicate.</param>
    /// <param name="merge">The operator, normally <see cref="Expression.AndAlso(Expression, Expression)"/>.</param>
    /// <returns>The combined predicate.</returns>
    public static Expression<Func<T, bool>> Combine<T>(
        Expression<Func<T, bool>> left,
        Expression<Func<T, bool>> right,
        Func<Expression, Expression, BinaryExpression> merge)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(T), "candidate");

        Expression leftBody = new ParameterRebinder(left.Parameters[0], parameter).Visit(left.Body);
        Expression rightBody = new ParameterRebinder(right.Parameters[0], parameter).Visit(right.Body);

        return Expression.Lambda<Func<T, bool>>(merge(leftBody, rightBody), parameter);
    }

    private sealed class ParameterRebinder : ExpressionVisitor
    {
        private readonly ParameterExpression _from;
        private readonly ParameterExpression _to;

        public ParameterRebinder(ParameterExpression from, ParameterExpression to)
        {
            _from = from;
            _to = to;
        }

        protected override Expression VisitParameter(ParameterExpression node) =>
            node == _from ? _to : base.VisitParameter(node);
    }
}
