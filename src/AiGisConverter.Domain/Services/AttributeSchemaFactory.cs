using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Services;

/// <summary>
/// Derives an attribute schema by inspecting the values actually present.
/// </summary>
/// <remarks>
/// <para>
/// CAD attributes are untyped text, and formats such as Shapefile demand a fixed schema declared
/// up front. Something has to look at the data and decide, and doing it once here &#8212; rather
/// than independently inside each exporter &#8212; is what stops the same column being written as
/// a number to GeoPackage and as text to Shapefile.
/// </para>
/// <para>
/// Inference is conservative. A column is numeric only if <em>every</em> non-empty value parses as
/// a number, because one alphanumeric asset tag in ten thousand rows still makes the column text.
/// Leading zeros force text as well: a postcode or a plot number that becomes an integer has lost
/// information that cannot be recovered.
/// </para>
/// </remarks>
public static class AttributeSchemaFactory
{
    /// <summary>Derives a schema from a source layer.</summary>
    /// <param name="layer">The layer to inspect.</param>
    /// <param name="sampleLimit">How many elements to sample. Zero or fewer inspects all of them.</param>
    /// <returns>The derived schema.</returns>
    public static GisAttributeSchema FromLayer(SourceLayer layer, int sampleLimit = 5000)
    {
        ArgumentNullException.ThrowIfNull(layer);

        Dictionary<string, TypeAccumulator> accumulators = new(StringComparer.OrdinalIgnoreCase);
        int inspected = 0;

        foreach (SourceElement element in layer.Elements)
        {
            if (sampleLimit > 0 && inspected >= sampleLimit)
            {
                break;
            }

            inspected++;

            foreach (KeyValuePair<string, object?> attribute in element.Attributes)
            {
                if (!accumulators.TryGetValue(attribute.Key, out TypeAccumulator? accumulator))
                {
                    accumulator = new TypeAccumulator();
                    accumulators[attribute.Key] = accumulator;
                }

                accumulator.Observe(attribute.Value);
            }
        }

        return new GisAttributeSchema(accumulators
            .Select(pair => FieldDefinition.Create(
                pair.Key,
                pair.Value.Resolve(),
                pair.Value.MaximumLength,
                pair.Value.SawNull))
            .OrderBy(static field => field.Name, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>Derives one schema covering every layer in a document.</summary>
    /// <param name="document">The document to inspect.</param>
    /// <param name="sampleLimitPerLayer">How many elements to sample per layer.</param>
    /// <returns>The merged schema.</returns>
    public static GisAttributeSchema FromDocument(SourceDocument document, int sampleLimitPerLayer = 5000)
    {
        ArgumentNullException.ThrowIfNull(document);

        GisAttributeSchema schema = GisAttributeSchema.Empty;

        foreach (SourceLayer layer in document.Layers)
        {
            schema = schema.Merge(FromLayer(layer, sampleLimitPerLayer));
        }

        return schema;
    }

    /// <summary>Tracks what has been seen in one column and decides its type.</summary>
    private sealed class TypeAccumulator
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

            // A leading zero is information. Parsing "007" as 7 silently corrupts plot and asset
            // numbering, so it pins the column to text regardless of what else is in it.
            if (text.Length > 1 && text[0] == '0' && text[1] != '.')
            {
                _forcedText = true;
            }

            if (_allBoolean && !bool.TryParse(text, out _))
            {
                _allBoolean = false;
            }

            if (_allInteger && !long.TryParse(
                text,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
            {
                _allInteger = false;
            }

            if (_allNumeric && !double.TryParse(
                text,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out _))
            {
                _allNumeric = false;
            }

            if (_allDateTime && !DateTimeOffset.TryParse(
                text,
                System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeUniversal,
                out _))
            {
                _allDateTime = false;
            }
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

            if (_allNumeric)
            {
                return AttributeDataType.Double;
            }

            return _allDateTime ? AttributeDataType.DateTime : AttributeDataType.Text;
        }
    }
}
