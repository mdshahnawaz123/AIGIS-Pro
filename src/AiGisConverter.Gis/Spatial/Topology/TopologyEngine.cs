using AiGisConverter.Gis.Spatial.Abstractions;
using NetTopologySuite.Geometries;
using NtsGeometry = NetTopologySuite.Geometries.Geometry;

namespace AiGisConverter.Gis.Spatial.Topology;

/// <summary>
/// Default <see cref="ITopologyEngine"/>, over NetTopologySuite's relate machinery.
/// </summary>
/// <remarks>
/// Every predicate is guarded against invalid input. A self-intersecting polygon makes the
/// underlying relate throw a <see cref="TopologyException"/> from deep inside the noding, and an
/// analysis of ten thousand parcels should not abort because one of them is bad. A predicate that
/// cannot be evaluated returns false, which is the conservative answer: it excludes the pair from
/// a result set rather than inventing a relationship.
/// </remarks>
public sealed class TopologyEngine : ITopologyEngine
{
    /// <inheritdoc />
    public bool Intersects(NtsGeometry left, NtsGeometry right) =>
        Evaluate(left, right, static (a, b) => a.Intersects(b));

    /// <inheritdoc />
    public bool Touches(NtsGeometry left, NtsGeometry right) =>
        Evaluate(left, right, static (a, b) => a.Touches(b));

    /// <inheritdoc />
    public bool Within(NtsGeometry inner, NtsGeometry outer) =>
        Evaluate(inner, outer, static (a, b) => a.Within(b));

    /// <inheritdoc />
    public bool Contains(NtsGeometry outer, NtsGeometry inner) =>
        Evaluate(outer, inner, static (a, b) => a.Contains(b));

    /// <inheritdoc />
    public bool Overlaps(NtsGeometry left, NtsGeometry right) =>
        Evaluate(left, right, static (a, b) => a.Overlaps(b));

    /// <inheritdoc />
    public bool Crosses(NtsGeometry left, NtsGeometry right) =>
        Evaluate(left, right, static (a, b) => a.Crosses(b));

    /// <inheritdoc />
    public bool Disjoint(NtsGeometry left, NtsGeometry right) =>
        Evaluate(left, right, static (a, b) => a.Disjoint(b));

    /// <inheritdoc />
    public string Relate(NtsGeometry left, NtsGeometry right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        try
        {
            return left.Relate(right).ToString();
        }
        catch (TopologyException)
        {
            // FF*FF**** is the matrix for "disjoint", the safe answer when nothing can be computed.
            return "FFFFFFFFF";
        }
    }

    /// <inheritdoc />
    public bool Relate(NtsGeometry left, NtsGeometry right, string pattern)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        ArgumentException.ThrowIfNullOrWhiteSpace(pattern);

        if (pattern.Length != 9)
        {
            throw new ArgumentException(
                "A DE-9IM pattern is exactly nine characters.",
                nameof(pattern));
        }

        try
        {
            return left.Relate(right, pattern);
        }
        catch (TopologyException)
        {
            return false;
        }
    }

    private static bool Evaluate(NtsGeometry left, NtsGeometry right, Func<NtsGeometry, NtsGeometry, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        if (left.IsEmpty || right.IsEmpty)
        {
            return false;
        }

        try
        {
            return predicate(left, right);
        }
        catch (TopologyException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
