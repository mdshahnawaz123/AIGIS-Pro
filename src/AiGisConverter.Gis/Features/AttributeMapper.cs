using System.Globalization;
using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Gis.Profiles;

namespace AiGisConverter.Gis.Features;

/// <summary>
/// Default <see cref="IAttributeMapper"/>.
/// </summary>
/// <remarks>
/// <para>
/// Type inference is conservative: a column is numeric only when every non-empty value parses.
/// One alphanumeric asset tag in ten thousand rows makes the whole column text, because a column
/// that is a number in one drawing and text in the next cannot be appended to an existing dataset.
/// </para>
/// <para>
/// Leading zeros force text unconditionally. A plot reference of <c>00742</c> read as the integer
/// 742 has lost information that cannot be recovered downstream, and it is precisely the kind of
/// corruption nobody notices until a land registry cross-reference fails.
/// </para>
/// </remarks>
public sealed class AttributeMapper : IAttributeMapper
{
    private const int DefaultSampleLimit = 5_000;

    /// <inheritdoc />
    public GisAttributeSchema BuildSchema(SourceLayer layer, ConversionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(layer);
        ArgumentNullException.ThrowIfNull(profile);

        Dictionary<string, ColumnProfile> columns = new(StringComparer.OrdinalIgnoreCase);
        int inspected = 0;

        foreach (SourceElement element in layer.Elements)
        {
            if (inspected++ >= DefaultSampleLimit)
            {
                break;
            }

            foreach (KeyValuePair<string, object?> attribute in element.Attributes)
            {
                string? fieldName = profile.ResolveFieldName(attribute.Key);

                if (fieldName is null)
                {
                    continue;
                }

                if (!columns.TryGetValue(fieldName, out ColumnProfile? column))
                {
                    column = new ColumnProfile();
                    columns[fieldName] = column;
                }

                column.Observe(attribute.Value);
            }
        }

        return new GisAttributeSchema(columns
            .Select(pair => FieldDefinition.Create(
                pair.Key,
                pair.Value.Resolve(),
                pair.Value.MaximumLength,
                pair.Value.SawNull))
            .OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <inheritdoc />
    public IReadOnlyDictionary<string, AttributeValue> Map(
        SourceElement element,
        GisAttributeSchema schema,
        ConversionProfile profile)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(profile);

        Dictionary<string, AttributeValue> mapped = new(StringComparer.OrdinalIgnoreCase);

        foreach (FieldDefinition field in schema.Fields)
        {
            mapped[field.Name] = AttributeValue.Null(field.DataType);
        }

        foreach (KeyValuePair<string, object?> attribute in element.Attributes)
        {
            string? fieldName = profile.ResolveFieldName(attribute.Key);

            if (fieldName is null || schema.Find(fieldName) is not { } field)
            {
                continue;
            }

            mapped[field.Name] = Coerce(attribute.Value, field.DataType);
        }

        return mapped;
    }

    /// <summary>Converts a loose value to the declared field type, falling back to text.</summary>
    private static AttributeValue Coerce(object? value, AttributeDataType type)
    {
        if (value is null)
        {
            return AttributeValue.Null(type);
        }

        string text = AttributeValue.FromObject(value).ToInvariantString();

        if (text.Length == 0)
        {
            return AttributeValue.Null(type);
        }

        return type switch
        {
            AttributeDataType.Integer when int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i)
                => AttributeValue.FromInteger(i),
            AttributeDataType.Long when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long l)
                => AttributeValue.FromLong(l),
            AttributeDataType.Double when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d)
                => AttributeValue.FromDouble(d),
            AttributeDataType.Boolean when bool.TryParse(text, out bool b)
                => AttributeValue.FromBoolean(b),
            AttributeDataType.DateTime when DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset t)
                => AttributeValue.FromDateTime(t),
            AttributeDataType.Text => AttributeValue.FromText(text),

            // The value contradicts the inferred schema. Keeping the text is lossless; forcing the
            // declared type would replace a real value with a null.
            _ => AttributeValue.FromText(text),
        };
    }

    /// <summary>Accumulates what has been seen in one column and decides its type.</summary>
    private sealed class ColumnProfile
    {
        private bool _sawValue;
        private bool _allInteger = true;
        private bool _allNumeric = true;
        private bool _allBoolean = true;
        private bool _allDateTime = true;
        private bool _forcedText;

        public bool SawNull { get; private set; }

        public int? MaximumLength { get; private set; }

        public void Observe(object? value)
        {
            if (value is null)
            {
                SawNull = true;
                return;
            }

            string text = AttributeValue.FromObject(value).ToInvariantString();

            if (text.Length == 0)
            {
                SawNull = true;
                return;
            }

            _sawValue = true;
            MaximumLength = MaximumLength is null ? text.Length : Math.Max(MaximumLength.Value, text.Length);

            if (HasLeadingZero(text))
            {
                _forcedText = true;
            }

            _allBoolean &= bool.TryParse(text, out _);
            _allInteger &= long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            _allNumeric &= double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            _allDateTime &= DateTimeOffset.TryParse(
                text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out _);
        }

        public AttributeDataType Resolve()
        {
            if (!_sawValue || _forcedText)
            {
                return AttributeDataType.Text;
            }

            if (_allBoolean)
            {
                return AttributeDataType.Boolean;
            }

            if (_allInteger)
            {
                return AttributeDataType.Long;
            }

            return _allNumeric
                ? AttributeDataType.Double
                : _allDateTime ? AttributeDataType.DateTime : AttributeDataType.Text;
        }

        /// <summary>Detects a leading zero that carries meaning, such as a plot or postcode.</summary>
        private static bool HasLeadingZero(string text) =>
            text.Length > 1 && text[0] == '0' && text[1] != '.';
    }
}
