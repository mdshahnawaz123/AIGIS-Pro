using AiGisConverter.Domain.Entities.QaQc;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Profiles;

namespace AiGisConverter.Gis.Abstractions;

/// <summary>
/// Everything a conversion stage needs to know, and the one place findings accumulate.
/// </summary>
/// <remarks>
/// Passed by reference through the whole pipeline so that a validator deep inside a feature loop
/// can record a finding without the enclosing stages having to thread a return value back out.
/// Thread-safe, because validation is the stage that gets parallelised.
/// </remarks>
public sealed class GisConversionContext
{
    private readonly List<ValidationIssue> _issues = [];
    private readonly object _gate = new();

    private int _featuresWritten;
    private int _featuresSkipped;
    private int _geometriesRepaired;

    /// <summary>Initializes a new instance of the <see cref="GisConversionContext"/> class.</summary>
    /// <param name="profile">The profile in force.</param>
    /// <param name="sourceCrs">The system the source coordinates are in.</param>
    /// <param name="targetCrs">The system the output is written in.</param>
    public GisConversionContext(ConversionProfile profile, CoordinateSystem sourceCrs, CoordinateSystem targetCrs)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(sourceCrs);
        ArgumentNullException.ThrowIfNull(targetCrs);

        Profile = profile;
        SourceCrs = sourceCrs;
        TargetCrs = targetCrs;
    }

    /// <summary>Gets the profile in force.</summary>
    public ConversionProfile Profile { get; }

    /// <summary>Gets the system the source coordinates are in.</summary>
    public CoordinateSystem SourceCrs { get; }

    /// <summary>Gets the system the output is written in.</summary>
    public CoordinateSystem TargetCrs { get; }

    /// <summary>Gets the semantic graph enrichment for the current conversion, if available.</summary>
    public AiGisConverter.Domain.Entities.Semantic.SemanticGraph? SemanticGraph { get; set; }

    /// <summary>Gets a value indicating whether a reprojection is required.</summary>
    public bool RequiresTransformation => SourceCrs != TargetCrs && !string.Equals(SourceCrs.Identifier, "Unknown", StringComparison.OrdinalIgnoreCase) && !string.Equals(TargetCrs.Identifier, "Unknown", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the number of features written.</summary>
    public int FeaturesWritten => Volatile.Read(ref _featuresWritten);

    /// <summary>Gets the number of features dropped.</summary>
    public int FeaturesSkipped => Volatile.Read(ref _featuresSkipped);

    /// <summary>Gets the number of geometries repaired.</summary>
    public int GeometriesRepaired => Volatile.Read(ref _geometriesRepaired);

    /// <summary>Gets the findings recorded so far.</summary>
    public IReadOnlyList<ValidationIssue> Issues
    {
        get
        {
            lock (_gate)
            {
                return [.. _issues];
            }
        }
    }

    /// <summary>Records a finding.</summary>
    /// <param name="issue">The finding.</param>
    public void Record(ValidationIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);

        lock (_gate)
        {
            _issues.Add(issue);
        }
    }

    /// <summary>Records several findings.</summary>
    /// <param name="issues">The findings.</param>
    public void Record(IEnumerable<ValidationIssue> issues)
    {
        ArgumentNullException.ThrowIfNull(issues);

        lock (_gate)
        {
            _issues.AddRange(issues);
        }
    }

    /// <summary>Counts a written feature.</summary>
    public void CountWritten() => Interlocked.Increment(ref _featuresWritten);

    /// <summary>Counts a dropped feature.</summary>
    public void CountSkipped() => Interlocked.Increment(ref _featuresSkipped);

    /// <summary>Counts a repaired geometry.</summary>
    public void CountRepaired() => Interlocked.Increment(ref _geometriesRepaired);
}
