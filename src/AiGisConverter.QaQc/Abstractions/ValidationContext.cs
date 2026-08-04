using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.QaQc.Options;

namespace AiGisConverter.QaQc.Abstractions;

/// <summary>
/// What a rule is given: one dataset, the thresholds in force, and a shared spatial index.
/// </summary>
/// <remarks>
/// The index is built once per dataset and handed to every rule that needs it. Letting each
/// topology rule build its own R-tree over a quarter of a million features would triple the cost
/// of the stage for no benefit.
/// </remarks>
public sealed class ValidationContext
{
    private readonly Lazy<IReadOnlyList<GisFeature>> _spatialFeatures;

    /// <summary>Initializes a new instance of the <see cref="ValidationContext"/> class.</summary>
    /// <param name="dataset">The dataset under inspection.</param>
    /// <param name="options">The thresholds in force.</param>
    public ValidationContext(GisDataset dataset, QaQcOptions options)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        ArgumentNullException.ThrowIfNull(options);

        Dataset = dataset;
        Options = options;

        _spatialFeatures = new Lazy<IReadOnlyList<GisFeature>>(
            () => [.. dataset.Features.Where(static f => f.Geometry is not null && !f.Geometry.IsEmpty)],
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets the dataset under inspection.</summary>
    public GisDataset Dataset { get; }

    /// <summary>Gets the thresholds in force.</summary>
    public QaQcOptions Options { get; }

    /// <summary>Gets the features that actually carry geometry.</summary>
    /// <remarks>
    /// Materialised once. A topology rule that filtered the dataset itself would repeat the scan
    /// for every rule, and a feature without geometry is invisible to all of them.
    /// </remarks>
    public IReadOnlyList<GisFeature> GeometricFeatures => _spatialFeatures.Value;

    /// <summary>Gets a value indicating whether the dataset is small enough for cross-feature work.</summary>
    public bool AllowsWholeDatasetRules =>
        Options.TopologyFeatureCeiling <= 0 || Dataset.Features.Count <= Options.TopologyFeatureCeiling;
}
