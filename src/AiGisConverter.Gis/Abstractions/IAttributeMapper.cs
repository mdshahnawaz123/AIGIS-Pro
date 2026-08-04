using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Profiles;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Turns a source element's loose attribute bag into typed, schema-conformant field values.
/// </summary>
public interface IAttributeMapper
{
    /// <summary>Derives the schema for a layer by inspecting its elements.</summary>
    /// <param name="layer">The source layer.</param>
    /// <param name="profile">The profile supplying naming and exclusion rules.</param>
    /// <returns>The derived schema.</returns>
    GisAttributeSchema BuildSchema(SourceLayer layer, ConversionProfile profile);

    /// <summary>Maps one element's attributes onto a schema.</summary>
    /// <param name="element">The source element.</param>
    /// <param name="schema">The schema to conform to.</param>
    /// <param name="profile">The profile supplying naming and exclusion rules.</param>
    /// <returns>The mapped values, keyed by output field name.</returns>
    IReadOnlyDictionary<string, AttributeValue> Map(
        SourceElement element,
        GisAttributeSchema schema,
        ConversionProfile profile);
}

/// <summary>Builds export-ready features from source elements.</summary>
public interface IFeatureBuilder
{
    /// <summary>
    /// Streams features for a layer.
    /// </summary>
    /// <remarks>
    /// Returns an async sequence rather than a list. A layer may hold a million elements, and
    /// materialising them so the exporter can iterate them once is the difference between a
    /// constant memory profile and an out-of-memory failure.
    /// </remarks>
    /// <param name="layer">The source layer.</param>
    /// <param name="featureClass">The target feature class.</param>
    /// <param name="schema">The attribute schema.</param>
    /// <param name="context">The conversion context.</param>
    /// <param name="cancellationToken">Token used to cancel the enumeration.</param>
    /// <returns>The features, produced lazily.</returns>
    IAsyncEnumerable<GisFeature> BuildAsync(
        SourceLayer layer,
        FeatureClass featureClass,
        GisAttributeSchema schema,
        GisConversionContext context,
        CancellationToken cancellationToken = default);
}
