using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// One column in a GIS attribute schema.
/// </summary>
/// <param name="Name">The field name.</param>
/// <param name="DataType">The storage type.</param>
/// <param name="MaxLength">Maximum text length, where the format enforces one. Null when unbounded.</param>
/// <param name="IsNullable">Whether the field may be absent on a feature.</param>
/// <remarks>
/// The domain records the field's intent. Format-specific mangling &#8212; Shapefile's
/// ten-character name limit, for instance &#8212; belongs to the exporter, so that one schema can
/// be written to several formats without the most restrictive one degrading the others.
/// </remarks>
public sealed record FieldDefinition(
    string Name,
    AttributeDataType DataType,
    int? MaxLength = null,
    bool IsNullable = true)
{
    /// <summary>Creates a field definition, rejecting a blank name.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="dataType">The storage type.</param>
    /// <param name="maxLength">Maximum text length, where applicable.</param>
    /// <param name="isNullable">Whether the field may be absent.</param>
    /// <returns>The created definition.</returns>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    public static FieldDefinition Create(
        string name,
        AttributeDataType dataType,
        int? maxLength = null,
        bool isNullable = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new FieldDefinition(name.Trim(), dataType, maxLength, isNullable);
    }

    /// <inheritdoc />
    public bool Equals(FieldDefinition? other) =>
        other is not null
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase)
        && DataType == other.DataType;

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Name), DataType);

    /// <inheritdoc />
    public override string ToString() => $"{Name} : {DataType}{(IsNullable ? "?" : string.Empty)}";
}
