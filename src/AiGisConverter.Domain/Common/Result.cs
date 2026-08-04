namespace AiGisConverter.Domain.Common;

/// <summary>
/// Represents the outcome of an operation without forcing callers to use exceptions for
/// expected failure modes such as an unreachable AI endpoint or an unreadable drawing.
/// </summary>
public class Result
{
    /// <summary>Initializes a new instance of the <see cref="Result"/> class.</summary>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The failure descriptor, or <see cref="Error.None"/> on success.</param>
    /// <exception cref="ArgumentException">The success flag and error value are inconsistent.</exception>
    protected Result(bool isSuccess, Error error)
    {
        ArgumentNullException.ThrowIfNull(error);

        if (isSuccess && error != Error.None)
        {
            throw new ArgumentException("A successful result cannot carry an error.", nameof(error));
        }

        if (!isSuccess && error == Error.None)
        {
            throw new ArgumentException("A failed result must carry an error.", nameof(error));
        }

        IsSuccess = isSuccess;
        Error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    public bool IsSuccess { get; }

    /// <summary>Gets a value indicating whether the operation failed.</summary>
    public bool IsFailure => !IsSuccess;

    /// <summary>Gets the failure descriptor. Equals <see cref="Error.None"/> when successful.</summary>
    public Error Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <returns>A successful <see cref="Result"/>.</returns>
    public static Result Success() => new(true, Error.None);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The failure descriptor.</param>
    /// <returns>A failed <see cref="Result"/>.</returns>
    public static Result Failure(Error error) => new(false, error);

    /// <summary>Creates a successful result carrying a value.</summary>
    /// <typeparam name="TValue">Type of the carried value.</typeparam>
    /// <param name="value">The value produced by the operation.</param>
    /// <returns>A successful <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> Success<TValue>(TValue value) => new(value, true, Error.None);

    /// <summary>Creates a failed result of the specified value type.</summary>
    /// <typeparam name="TValue">Type of the value that would have been carried.</typeparam>
    /// <param name="error">The failure descriptor.</param>
    /// <returns>A failed <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> Failure<TValue>(Error error) => new(default, false, error);
}

/// <summary>
/// Represents the outcome of an operation that produces a value when successful.
/// </summary>
/// <typeparam name="TValue">Type of the carried value.</typeparam>
public sealed class Result<TValue> : Result
{
    private readonly TValue? _value;

    /// <summary>Initializes a new instance of the <see cref="Result{TValue}"/> class.</summary>
    /// <param name="value">The carried value, or <see langword="null"/> on failure.</param>
    /// <param name="isSuccess">Whether the operation succeeded.</param>
    /// <param name="error">The failure descriptor, or <see cref="Error.None"/> on success.</param>
    internal Result(TValue? value, bool isSuccess, Error error)
        : base(isSuccess, error) => _value = value;

    /// <summary>Gets the carried value.</summary>
    /// <exception cref="InvalidOperationException">The result represents a failure.</exception>
    public TValue Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException($"Cannot read the value of a failed result. {Error}");

    /// <summary>Implicitly wraps a value in a successful result.</summary>
    /// <param name="value">The value to wrap.</param>
    public static implicit operator Result<TValue>(TValue value) => Success(value);

    /// <summary>Wraps a value in a successful result.</summary>
    /// <param name="value">The value to wrap.</param>
    /// <returns>A successful <see cref="Result{TValue}"/>.</returns>
    public static Result<TValue> FromValue(TValue value) => Success(value);
}
