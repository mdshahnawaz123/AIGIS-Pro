using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Entities.Gis;

/// <summary>
/// A homogeneous set of features destined for one output layer. Immutable.
/// </summary>
/// <remarks>
/// One dataset maps to one Shapefile, one GeoPackage table or one GeoJSON file. Its
/// <see cref="FeatureClass"/> fixes both the name and the geometry family, because no mainstream
/// GIS format permits mixed geometry in a single layer.
/// </remarks>
public sealed class GisDataset
{
    /// <summary>Initializes a new instance of the <see cref="GisDataset"/> class.</summary>
    /// <param name="featureClass">The class every feature belongs to.</param>
    /// <param name="coordinateSystem">The system the geometries are expressed in.</param>
    /// <param name="schema">The attribute schema.</param>
    /// <param name="features">The features.</param>
    public GisDataset(
        FeatureClass featureClass,
        CoordinateSystem coordinateSystem,
        GisAttributeSchema schema,
        IEnumerable<GisFeature> features)
    {
        ArgumentNullException.ThrowIfNull(featureClass);
        ArgumentNullException.ThrowIfNull(coordinateSystem);
        ArgumentNullException.ThrowIfNull(schema);
        ArgumentNullException.ThrowIfNull(features);

        FeatureClass = featureClass;
        CoordinateSystem = coordinateSystem;
        Schema = schema;
        Features = [.. features];

        Extent computed = ValueObjects.Extent.Empty;

        foreach (GisFeature feature in Features)
        {
            computed = computed.Union(feature.Extent);
        }

        Extent = computed;
    }

    /// <summary>Gets the class every feature in the dataset belongs to.</summary>
    public FeatureClass FeatureClass { get; }

    /// <summary>Gets the coordinate system the geometries are expressed in.</summary>
    public CoordinateSystem CoordinateSystem { get; }

    /// <summary>Gets the attribute schema.</summary>
    public GisAttributeSchema Schema { get; }

    /// <summary>Gets the features.</summary>
    public IReadOnlyList<GisFeature> Features { get; }

    /// <summary>Gets the combined bounding box of every feature.</summary>
    public Extent Extent { get; }

    /// <summary>Gets a value indicating whether the dataset holds no features.</summary>
    public bool IsEmpty => Features.Count == 0;

    /// <inheritdoc />
    public override string ToString() => $"{FeatureClass} ({Features.Count} features, {CoordinateSystem.Identifier})";
}
