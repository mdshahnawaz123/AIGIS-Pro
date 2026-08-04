using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.QaQc.Abstractions;
using NetTopologySuite.Geometries;

namespace AiGisConverter.QaQc.Rules.Crs;

/// <summary>
/// Reports coordinates that cannot be valid in the declared coordinate system.
/// </summary>
/// <remarks>
/// <para>
/// The classic failure is a projected dataset labelled as WGS 84. Eastings of 528,000 are not
/// longitudes, but nothing downstream objects: the file opens, the features render somewhere in
/// the Pacific, and the error is only obvious to someone who already knows where the site is.
/// </para>
/// <para>
/// The converse &#8212; geographic data labelled as projected &#8212; is caught by the
/// near-origin check, because a whole dataset within a few hundred units of 0,0 in a projected
/// system means the georeference was never applied.
/// </para>
/// </remarks>
public sealed class CoordinateRangeRule : IValidationRule
{
    private const double MaximumLongitude = 180d;
    private const double MaximumLatitude = 90d;

    /// <summary>
    /// Distance from the projected origin below which a dataset is assumed to be ungeoreferenced.
    /// </summary>
    /// <remarks>
    /// No national grid places real survey data within a kilometre of its false origin, so a whole
    /// dataset sitting there is drawing coordinates, not ground coordinates.
    /// </remarks>
    private const double SuspiciousOriginRadius = 1_000d;

    /// <inheritdoc />
    public string RuleId => "Crs.CoordinateOutOfRange";

    /// <inheritdoc />
    public string DisplayName => "Coordinates inconsistent with the declared system";

    /// <inheritdoc />
    public IssueCategory Category => IssueCategory.Crs;

    /// <inheritdoc />
    public bool RequiresWholeDataset => false;

    /// <inheritdoc />
    public IEnumerable<ValidationIssue> Validate(
        ValidationContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        CoordinateSystem system = context.Dataset.CoordinateSystem;
        Extent extent = context.Dataset.Extent;

        if (extent.IsEmpty)
        {
            yield break;
        }

        if (system.IsGeographic)
        {
            foreach (ValidationIssue issue in CheckGeographic(context, system, cancellationToken))
            {
                yield return issue;
            }

            yield break;
        }

        bool nearOrigin = Math.Abs(extent.CentreX) < SuspiciousOriginRadius
                          && Math.Abs(extent.CentreY) < SuspiciousOriginRadius;

        if (nearOrigin)
        {
            yield return ValidationIssue.Create(
                IssueSeverity.Error,
                IssueCategory.Crs,
                RuleId,
                $"The dataset is centred on {extent.CentreX:G6}, {extent.CentreY:G6} in " +
                $"{system.Identifier}, within {SuspiciousOriginRadius:N0} units of the projected origin.")
                .At(extent.CentreX, extent.CentreY)
                .WithRemediation(
                    "The drawing was probably never georeferenced. Set the source coordinate system, " +
                    "or supply a projection sidecar.");
        }
    }

    private IEnumerable<ValidationIssue> CheckGeographic(
        ValidationContext context,
        CoordinateSystem system,
        CancellationToken cancellationToken)
    {
        Extent extent = context.Dataset.Extent;

        if (Math.Abs(extent.MinX) <= MaximumLongitude && Math.Abs(extent.MaxX) <= MaximumLongitude &&
            Math.Abs(extent.MinY) <= MaximumLatitude && Math.Abs(extent.MaxY) <= MaximumLatitude)
        {
            yield break;
        }

        // One finding for the dataset, then a bounded sample of offenders. A projected dataset
        // mislabelled as geographic puts every feature out of range, and one line per feature
        // would be a hundred thousand lines saying the same thing.
        yield return ValidationIssue.Create(
            IssueSeverity.Critical,
            IssueCategory.Crs,
            RuleId,
            $"'{context.Dataset.FeatureClass.Name}' is declared as {system.Identifier}, which is " +
            $"geographic, but its extent is {extent}. These are not longitudes and latitudes.")
            .WithRemediation(
                "The data is almost certainly projected. Set the correct source system and re-run.");

        int reported = 0;

        foreach (GisFeature feature in context.GeometricFeatures)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (reported >= 5)
            {
                yield break;
            }

            Coordinate? offender = feature.Geometry!.Coordinates
                .FirstOrDefault(c => Math.Abs(c.X) > MaximumLongitude || Math.Abs(c.Y) > MaximumLatitude);

            if (offender is null)
            {
                continue;
            }

            reported++;

            yield return ValidationIssue.Create(
                IssueSeverity.Critical,
                IssueCategory.Crs,
                RuleId,
                $"Feature '{feature.Id}' has a coordinate at {offender.X:G8}, {offender.Y:G8}.")
                .ForFeature(feature.Id)
                .At(offender.X, offender.Y);
        }
    }
}
