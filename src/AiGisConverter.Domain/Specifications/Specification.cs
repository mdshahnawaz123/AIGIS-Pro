using System.Linq.Expressions;

namespace AiGisConverter.Domain.Specifications;

/// <summary>
/// Base class giving specifications their boolean algebra.
/// </summary>
/// <typeparam name="T">The entity the specification selects.</typeparam>
public abstract class Specification<T> : ISpecification<T>
{
    private Func<T, bool>? _compiled;

    /// <inheritdoc />
    public abstract Expression<Func<T, bool>> ToExpression();

    /// <inheritdoc />
    public bool IsSatisfiedBy(T candidate)
    {
        _compiled ??= ToExpression().Compile();

        return _compiled(candidate);
    }

    /// <summary>Combines this specification with another using logical AND.</summary>
    /// <param name="other">The specification to combine with.</param>
    /// <returns>The combined specification.</returns>
    public Specification<T> And(Specification<T> other) => new AndSpecification<T>(this, other);

    /// <summary>Combines this specification with another using logical OR.</summary>
    /// <param name="other">The specification to combine with.</param>
    /// <returns>The combined specification.</returns>
    public Specification<T> Or(Specification<T> other) => new OrSpecification<T>(this, other);

    /// <summary>Negates this specification.</summary>
    /// <returns>The negated specification.</returns>
    public Specification<T> Not() => new NotSpecification<T>(this);

    /// <summary>Creates a specification from an inline expression.</summary>
    /// <param name="expression">The predicate.</param>
    /// <returns>The created specification.</returns>
    public static Specification<T> FromExpression(Expression<Func<T, bool>> expression) =>
        new InlineSpecification<T>(expression);
}

/// <summary>A specification wrapping a single inline expression.</summary>
/// <typeparam name="T">The entity the specification selects.</typeparam>
internal sealed class InlineSpecification<T> : Specification<T>
{
    private readonly Expression<Func<T, bool>> _expression;

    public InlineSpecification(Expression<Func<T, bool>> expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
        _expression = expression;
    }

    public override Expression<Func<T, bool>> ToExpression() => _expression;
}

/// <summary>The conjunction of two specifications.</summary>
/// <typeparam name="T">The entity the specification selects.</typeparam>
internal sealed class AndSpecification<T> : Specification<T>
{
    private readonly Specification<T> _left;
    private readonly Specification<T> _right;

    public AndSpecification(Specification<T> left, Specification<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _right = right;
    }

    public override Expression<Func<T, bool>> ToExpression() =>
        ExpressionComposer.Combine(_left.ToExpression(), _right.ToExpression(), Expression.AndAlso);
}

/// <summary>The disjunction of two specifications.</summary>
/// <typeparam name="T">The entity the specification selects.</typeparam>
internal sealed class OrSpecification<T> : Specification<T>
{
    private readonly Specification<T> _left;
    private readonly Specification<T> _right;

    public OrSpecification(Specification<T> left, Specification<T> right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        _left = left;
        _right = right;
    }

    public override Expression<Func<T, bool>> ToExpression() =>
        ExpressionComposer.Combine(_left.ToExpression(), _right.ToExpression(), Expression.OrElse);
}

/// <summary>The negation of a specification.</summary>
/// <typeparam name="T">The entity the specification selects.</typeparam>
internal sealed class NotSpecification<T> : Specification<T>
{
    private readonly Specification<T> _inner;

    public NotSpecification(Specification<T> inner)
    {
        ArgumentNullException.ThrowIfNull(inner);
        _inner = inner;
    }

    public override Expression<Func<T, bool>> ToExpression()
    {
        Expression<Func<T, bool>> expression = _inner.ToExpression();

        return Expression.Lambda<Func<T, bool>>(
            Expression.Not(expression.Body),
            expression.Parameters);
    }
}
