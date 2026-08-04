using System.Globalization;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// A typed attribute value.
/// </summary>
/// <remarks>
/// <para>
/// Source attributes arrive as loosely typed text. Carrying the declared
/// <see cref="AttributeDataType"/> alongside the value means the export layer does not have to
/// re-infer a type per row &#8212; which is how a column of postcodes ends up as a numeric field
/// in one file and text in the next.
/// </para>
/// <para>
/// A null value is representable and keeps its declared type, because "this feature has no
/// recorded diameter" and "this feature has no diameter field" are different statements.
/// </para>
/// </remarks>
public readonly record struct AttributeValue
{
    private AttributeValue(object? value, AttributeDataType dataType)
    {
        RawValue = value;
        DataType = dataType;
    }

    /// <summary>Gets the underlying value, which may be null.</summary>
    public object? RawValue { get; }

    /// <summary>Gets the declared storage type.</summary>
    public AttributeDataType DataType { get; }

    /// <summary>Gets a value indicating whether the value is absent.</summary>
    public bool IsNull => RawValue is null;

    /// <summary>Creates a text value.</summary>
    /// <param name="value">The text, which may be null.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue FromText(string? value) => new(value, AttributeDataType.Text);

    /// <summary>Creates a 32-bit integer value.</summary>
    /// <param name="value">The number, which may be null.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue FromInteger(int? value) => new(value, AttributeDataType.Integer);

    /// <summary>Creates a 64-bit integer value.</summary>
    /// <param name="value">The number, which may be null.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue FromLong(long? value) => new(value, AttributeDataType.Long);

    /// <summary>Creates a floating-point value.</summary>
    /// <param name="value">The number, which may be null.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue FromDouble(double? value) => new(value, AttributeDataType.Double);

    /// <summary>Creates a boolean value.</summary>
    /// <param name="value">The flag, which may be null.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue FromBoolean(bool? value) => new(value, AttributeDataType.Boolean);

    /// <summary>Creates a timestamp value.</summary>
    /// <param name="value">The instant, which may be null.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue FromDateTime(DateTimeOffset? value) => new(value, AttributeDataType.DateTime);

    /// <summary>Creates a null value of a declared type.</summary>
    /// <param name="dataType">The declared type.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue Null(AttributeDataType dataType) => new(null, dataType);

    /// <summary>
    /// Wraps an arbitrary object, inferring the type from its runtime type.
    /// </summary>
    /// <remarks>Used at the reader boundary, where attributes arrive untyped.</remarks>
    /// <param name="value">The value to wrap.</param>
    /// <returns>The created value.</returns>
    public static AttributeValue FromObject(object? value) => value switch
    {
        null => new AttributeValue(null, AttributeDataType.Text),
        string text => FromText(text),
        bool flag => FromBoolean(flag),
        int number => FromInteger(number),
        long number => FromLong(number),
        short number => FromInteger(number),
        float number => FromDouble(number),
        double number => FromDouble(number),
        decimal number => FromDouble((double)number),
        DateTimeOffset timestamp => FromDateTime(timestamp),
        DateTime timestamp => FromDateTime(new DateTimeOffset(timestamp.ToUniversalTime(), TimeSpan.Zero)),
        _ => FromText(Convert.ToString(value, CultureInfo.InvariantCulture)),
    };

    /// <summary>Renders the value as invariant text, suitable for CSV and WKT-adjacent output.</summary>
    /// <returns>The rendered value, or an empty string when null.</returns>
    public string ToInvariantString() => RawValue switch
    {
        null => string.Empty,
        string text => text,
        bool flag => flag ? "true" : "false",
        DateTimeOffset timestamp => timestamp.ToString("O", CultureInfo.InvariantCulture),
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => Convert.ToString(RawValue, CultureInfo.InvariantCulture) ?? string.Empty,
    };

    /// <inheritdoc />
    public override string ToString() => IsNull ? $"null ({DataType})" : $"{ToInvariantString()} ({DataType})";
}
