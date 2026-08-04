using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Entities.Gis;

/// <summary>
/// The column definition shared by every feature in a dataset. Immutable.
/// </summary>
/// <remarks>
/// Schemas are combined rather than mutated: <see cref="Merge"/> returns a new schema. That keeps
/// a schema safe to share between the classification, validation and export stages running
/// concurrently over the same dataset.
/// </remarks>
public sealed class GisAttributeSchema
{
    private readonly Dictionary<string, FieldDefinition> _byName;

    /// <summary>Initializes a new instance of the <see cref="GisAttributeSchema"/> class.</summary>
    /// <param name="fields">The field definitions. Later duplicates of a name are ignored.</param>
    public GisAttributeSchema(IEnumerable<FieldDefinition> fields)
    {
        ArgumentNullException.ThrowIfNull(fields);

        _byName = new Dictionary<string, FieldDefinition>(StringComparer.OrdinalIgnoreCase);
        List<FieldDefinition> ordered = [];

        foreach (FieldDefinition field in fields)
        {
            if (_byName.TryAdd(field.Name, field))
            {
                ordered.Add(field);
            }
        }

        Fields = ordered;
    }

    /// <summary>Gets the empty schema.</summary>
    public static GisAttributeSchema Empty { get; } = new([]);

    /// <summary>Gets the fields, in declaration order.</summary>
    public IReadOnlyList<FieldDefinition> Fields { get; }

    /// <summary>Gets the number of fields.</summary>
    public int Count => Fields.Count;

    /// <summary>Finds a field by name, case-insensitively.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The definition, or <see langword="null"/> when the schema has no such field.</returns>
    public FieldDefinition? Find(string name) =>
        string.IsNullOrWhiteSpace(name) ? null : _byName.GetValueOrDefault(name);

    /// <summary>Determines whether the schema declares a field.</summary>
    /// <param name="name">The field name.</param>
    /// <returns><see langword="true"/> when the field is declared.</returns>
    public bool Contains(string name) => Find(name) is not null;

    /// <summary>
    /// Combines two schemas, widening any field whose type differs between them.
    /// </summary>
    /// <remarks>
    /// Widening is deliberately conservative: a field that is an integer in one drawing and text
    /// in another becomes text, because losing precision is recoverable and losing the value is
    /// not.
    /// </remarks>
    /// <param name="other">The schema to combine with.</param>
    /// <returns>A new combined schema.</returns>
    public GisAttributeSchema Merge(GisAttributeSchema other)
    {
        ArgumentNullException.ThrowIfNull(other);

        List<FieldDefinition> combined = [.. Fields];

        foreach (FieldDefinition field in other.Fields)
        {
            FieldDefinition? existing = Find(field.Name);

            if (existing is null)
            {
                combined.Add(field);
                continue;
            }

            if (existing.DataType == field.DataType)
            {
                continue;
            }

            int index = combined.FindIndex(f =>
                string.Equals(f.Name, field.Name, StringComparison.OrdinalIgnoreCase));

            combined[index] = existing with
            {
                DataType = Widen(existing.DataType, field.DataType),
                MaxLength = Max(existing.MaxLength, field.MaxLength),
                IsNullable = existing.IsNullable || field.IsNullable,
            };
        }

        return new GisAttributeSchema(combined);
    }

    /// <summary>Returns a schema with an additional field.</summary>
    /// <param name="field">The field to add. Ignored when the name is already declared.</param>
    /// <returns>A new schema.</returns>
    public GisAttributeSchema With(FieldDefinition field)
    {
        ArgumentNullException.ThrowIfNull(field);

        return Contains(field.Name) ? this : new GisAttributeSchema([.. Fields, field]);
    }

    /// <summary>Chooses the type that can hold values of both inputs.</summary>
    private static AttributeDataType Widen(AttributeDataType left, AttributeDataType right)
    {
        if (left == right)
        {
            return left;
        }

        bool bothNumeric =
            IsNumeric(left) && IsNumeric(right);

        if (!bothNumeric)
        {
            return AttributeDataType.Text;
        }

        if (left == AttributeDataType.Double || right == AttributeDataType.Double)
        {
            return AttributeDataType.Double;
        }

        return AttributeDataType.Long;
    }

    private static bool IsNumeric(AttributeDataType type) =>
        type is AttributeDataType.Integer or AttributeDataType.Long or AttributeDataType.Double;

    private static int? Max(int? left, int? right) => (left, right) switch
    {
        (null, null) => null,
        (null, _) => right,
        (_, null) => left,
        _ => Math.Max(left.Value, right.Value),
    };
}
