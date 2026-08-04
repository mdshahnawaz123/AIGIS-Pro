using AiGisConverter.Domain.Enums;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Domain.Entities.Source;

/// <summary>
/// One element read from a source: a CAD entity, a Revit instance, an IFC product, a point-cloud
/// cluster or a PDF vector path.
/// </summary>
/// <remarks>
/// <para>
/// Deliberately format-neutral. A reader plugin reduces whatever its SDK exposes to geometry plus
/// attributes, so nothing downstream needs to know the source format.
/// </para>
/// <para>
/// This type is mutable during construction, unlike the rest of the domain. A reader streams
/// hundreds of thousands of elements and populates each in stages; forcing an immutable
/// construction would mean an intermediate builder object per element, which is allocation the
/// hot path cannot justify. Once a document has been handed to the pipeline it is treated as
/// read-only, and its collections are exposed as such so that treatment is enforced rather than
/// merely assumed.
/// </para>
/// </remarks>
public sealed class SourceElement
{
    private readonly Dictionary<string, object?> _attributes =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Initializes a new instance of the <see cref="SourceElement"/> class.</summary>
    /// <param name="id">Identifier that is stable within the source document.</param>
    /// <param name="geometryKind">The element's geometry family.</param>
    public SourceElement(string id, GeometryKind geometryKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        Id = id;
        GeometryKind = geometryKind;
    }

    /// <summary>Gets the identifier, stable within the source document.</summary>
    public string Id { get; }

    /// <summary>Gets the element's geometry family.</summary>
    public GeometryKind GeometryKind { get; }

    /// <summary>Gets or sets the element geometry in source coordinates. Null for pure annotation.</summary>
    public Geometry? Geometry { get; set; }

    /// <summary>Gets or sets the source-native type name, for example <c>LWPOLYLINE</c> or <c>IfcWall</c>.</summary>
    public string? NativeType { get; set; }

    /// <summary>Gets or sets the text carried by the element, when it is annotation or has a label.</summary>
    public string? Text { get; set; }

    /// <summary>Gets the element's attributes, which become GIS feature fields.</summary>
    public IReadOnlyDictionary<string, object?> Attributes => _attributes;

    /// <summary>Sets an attribute, replacing any existing value for the name.</summary>
    /// <param name="name">The attribute name, matched case-insensitively.</param>
    /// <param name="value">The value, which may be null.</param>
    public void SetAttribute(string name, object? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        _attributes[name] = value;
    }

    /// <summary>Copies a set of attributes onto the element.</summary>
    /// <param name="attributes">The attributes to copy.</param>
    public void SetAttributes(IEnumerable<KeyValuePair<string, object?>> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);

        foreach (KeyValuePair<string, object?> attribute in attributes)
        {
            SetAttribute(attribute.Key, attribute.Value);
        }
    }

    /// <inheritdoc />
    public override string ToString() => $"{GeometryKind} {Id}";
}
