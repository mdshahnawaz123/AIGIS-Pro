using System.Globalization;

namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// A normalised classification confidence in the closed interval <c>[0, 1]</c>.
/// </summary>
public readonly record struct Confidence : IComparable<Confidence>, IComparable
{
    /// <summary>Zero confidence.</summary>
    public static readonly Confidence Zero = new(0d);

    /// <summary>Full confidence.</summary>
    public static readonly Confidence Certain = new(1d);

    private Confidence(double value) => Value = value;

    /// <summary>Gets the confidence score in the closed interval <c>[0, 1]</c>.</summary>
    public double Value { get; }

    /// <summary>Creates a confidence from a score, rejecting values outside <c>[0, 1]</c>.</summary>
    /// <param name="score">The score to wrap.</param>
    /// <returns>The created <see cref="Confidence"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The score is NaN or outside <c>[0, 1]</c>.</exception>
    public static Confidence FromScore(double score)
    {
        if (double.IsNaN(score) || score < 0d || score > 1d)
        {
            throw new ArgumentOutOfRangeException(nameof(score), score, "Confidence must be within [0, 1].");
        }

        return new Confidence(score);
    }

    /// <summary>Creates a confidence from a score, clamping values outside <c>[0, 1]</c>.</summary>
    /// <remarks>Used when consuming third-party model output, which is not always well-formed.</remarks>
    /// <param name="score">The score to clamp and wrap.</param>
    /// <returns>The created <see cref="Confidence"/>.</returns>
    public static Confidence Clamp(double score) =>
        double.IsNaN(score) ? Zero : new Confidence(Math.Clamp(score, 0d, 1d));

    /// <inheritdoc />
    public int CompareTo(Confidence other) => Value.CompareTo(other.Value);

    /// <inheritdoc />
    public int CompareTo(object? obj) => obj switch
    {
        null => 1,
        Confidence other => CompareTo(other),
        _ => throw new ArgumentException($"Object must be of type {nameof(Confidence)}.", nameof(obj)),
    };

    /// <summary>Determines whether the left operand is less than the right operand.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is smaller.</returns>
    public static bool operator <(Confidence left, Confidence right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether the left operand is less than or equal to the right operand.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is smaller or equal.</returns>
    public static bool operator <=(Confidence left, Confidence right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether the left operand is greater than the right operand.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is greater.</returns>
    public static bool operator >(Confidence left, Confidence right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether the left operand is greater than or equal to the right operand.</summary>
    /// <param name="left">Left operand.</param>
    /// <param name="right">Right operand.</param>
    /// <returns><see langword="true"/> when <paramref name="left"/> is greater or equal.</returns>
    public static bool operator >=(Confidence left, Confidence right) => left.CompareTo(right) >= 0;

    /// <inheritdoc />
    public override string ToString() => Value.ToString("P1", CultureInfo.InvariantCulture);
}
