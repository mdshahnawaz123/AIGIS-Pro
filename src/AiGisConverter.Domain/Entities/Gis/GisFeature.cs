using AiGisConverter.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Domain.Entities.Gis;

/// <summary>
/// A converted, classified feature ready for export. Immutable.
/// </summary>
/// <remarks>
/// <para>
/// Immutability is appropriate here in a way it is not for the source model. A
/// <c>SourceElement</c> is assembled incrementally while streaming a drawing; a
/// <see cref="GisFeature"/> is produced once, at the end of the pipeline, and is then read
/// concurrently by validation and by one or more exporters. Making it read-only removes any
/// question about whether the GeoPackage writer can safely run beside the Shapefile writer.
/// </para>
/// <para>
/// The geometry itself is a NetTopologySuite object and is mutable by design of that library.
/// Treat it as read-only: exporters must not modify a geometry they did not create.
/// </para>
/// </remarks>
public sealed class GisFeature
{
    private readonly Dictionary<string, AttributeValue> _attributes;

    /// <summary>Initializes a new instance of the <see cref="GisFeature"/> class.</summary>
    /// <param name="id">Identifier, unique within the dataset.</param>
    /// <param name="featureClass">The class this feature belongs to.</param>
    /// <param name="geometry">The geometry, in the dataset's coordinate system.</param>
    /// <param name="attributes">The attribute values.</param>
    /// <param name="sourceLayer">The source layer the feature came from.</param>
    /// <param name="sourceElementId">The source element the feature came from.</param>
    /// <param name="classification">The classification result that produced this feature class.</param>
    public GisFeature(
        string id,
        FeatureClass featureClass,
        Geometry? geometry,
        IEnumerable<KeyValuePair<string, AttributeValue>> attributes,
        LayerName sourceLayer,
        string sourceElementId,
        AiGisConverter.Domain.Entities.Ai.ClassificationResult? classification = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(featureClass);
        ArgumentNullException.ThrowIfNull(attributes);
        ArgumentNullException.ThrowIfNull(sourceLayer);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceElementId);

        Id = id;
        FeatureClass = featureClass;
        Geometry = geometry;
        SourceLayer = sourceLayer;
        SourceElementId = sourceElementId;
        Classification = classification;

        _attributes = new Dictionary<string, AttributeValue>(StringComparer.OrdinalIgnoreCase);

        foreach (KeyValuePair<string, AttributeValue> pair in attributes)
        {
            _attributes[pair.Key] = pair.Value;
        }
    }

    /// <summary>Gets the identifier, unique within the dataset.</summary>
    public string Id { get; }

    /// <summary>Gets the class this feature belongs to.</summary>
    public FeatureClass FeatureClass { get; }

    /// <summary>Gets the geometry. Null for features that carry attributes only.</summary>
    public Geometry? Geometry { get; }

    /// <summary>Gets the source layer the feature came from, retained for traceability.</summary>
    public LayerName SourceLayer { get; }

    /// <summary>Gets the source element the feature came from, retained for traceability.</summary>
    public string SourceElementId { get; }

    /// <summary>Gets the AI classification result, if any.</summary>
    public AiGisConverter.Domain.Entities.Ai.ClassificationResult? Classification { get; }

    /// <summary>Gets the semantic feature enrichment, if any.</summary>
    public AiGisConverter.Domain.Entities.Semantic.SemanticFeature? SemanticFeature { get; init; }

    /// <summary>Gets the attribute values.</summary>
    public IReadOnlyDictionary<string, AttributeValue> Attributes => _attributes;

    /// <summary>Gets the feature's bounding box, or the empty extent when it has no geometry.</summary>
    public Extent Extent
    {
        get
        {
            if (Geometry is null || Geometry.IsEmpty)
            {
                return ValueObjects.Extent.Empty;
            }

            Envelope envelope = Geometry.EnvelopeInternal;

            return ValueObjects.Extent.Create(
                envelope.MinX,
                envelope.MinY,
                envelope.MaxX,
                envelope.MaxY);
        }
    }

    /// <summary>Reads an attribute.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The value, or a null text value when the feature has no such attribute.</returns>
    public AttributeValue GetAttribute(string name) =>
        _attributes.TryGetValue(name, out AttributeValue value)
            ? value
            : AttributeValue.Null(Enums.AttributeDataType.Text);

    /// <summary>Returns a copy of this feature with one attribute replaced or added.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="value">The new value.</param>
    /// <returns>A new feature.</returns>
    public GisFeature WithAttribute(string name, AttributeValue value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        Dictionary<string, AttributeValue> copy = new(_attributes, StringComparer.OrdinalIgnoreCase)
        {
            [name] = value,
        };

        return new GisFeature(Id, FeatureClass, Geometry, copy, SourceLayer, SourceElementId, Classification) { SemanticFeature = SemanticFeature };
    }

    /// <inheritdoc />
    public override string ToString() => $"{FeatureClass.Name}#{Id}";
}
