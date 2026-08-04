using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// A target GIS feature class: a name paired with the geometry family it holds.
/// </summary>
/// <remarks>
/// Geometry is part of the identity, not an attribute of it. Most GIS formats cannot mix geometry
/// families in one layer, so "Building" as polygons and "Building" as points are two different
/// destinations and must not be merged during export.
/// </remarks>
/// <param name="Name">The feature class name.</param>
/// <param name="Geometry">The geometry family the class holds.</param>
public sealed record FeatureClass(string Name, GeometryKind Geometry)
{
    /// <summary>The class assigned when nothing else fits.</summary>
    public const string UnclassifiedName = "Unclassified";

    /// <summary>Creates a feature class, rejecting a blank name.</summary>
    /// <param name="name">The feature class name.</param>
    /// <param name="geometry">The geometry family.</param>
    /// <returns>The created feature class.</returns>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    public static FeatureClass Create(string name, GeometryKind geometry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new FeatureClass(name.Trim(), geometry);
    }

    /// <summary>Creates the unclassified class for a geometry family.</summary>
    /// <param name="geometry">The geometry family.</param>
    /// <returns>The unclassified feature class.</returns>
    public static FeatureClass Unclassified(GeometryKind geometry) => new(UnclassifiedName, geometry);

    /// <inheritdoc />
    public bool Equals(FeatureClass? other) =>
        other is not null
        && Geometry == other.Geometry
        && string.Equals(Name, other.Name, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() =>
        HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Name), Geometry);

    /// <inheritdoc />
    public override string ToString() => $"{Name} [{Geometry}]";
}
