using System.Globalization;
using AiGisConverter.Domain.Validation;

namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// An axis-aligned bounding box, optionally with a vertical range.
/// </summary>
/// <remarks>
/// A readonly record struct: extents are created in large numbers while scanning a drawing, and
/// making them a value type keeps that allocation-free. The empty extent is the additive identity
/// &#8212; <c>Extent.Empty.Union(x) == x</c> &#8212; which lets accumulation start from nothing
/// without a special first-iteration branch.
/// </remarks>
public readonly record struct Extent
{
    // Stored as "has value" rather than "is empty" so that default(Extent) is the empty extent.
    // The inverse would make an uninitialised struct claim to be a zero-sized box at the origin,
    // which silently drags every computed extent back to 0,0.
    private readonly bool _hasValue;

    private Extent(double minX, double minY, double maxX, double maxY, double minZ, double maxZ, bool hasValue)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        MinZ = minZ;
        MaxZ = maxZ;
        _hasValue = hasValue;
    }

    /// <summary>Gets the empty extent, which contains nothing and absorbs into any union.</summary>
    public static Extent Empty { get; } = new(
        double.PositiveInfinity,
        double.PositiveInfinity,
        double.NegativeInfinity,
        double.NegativeInfinity,
        double.PositiveInfinity,
        double.NegativeInfinity,
        hasValue: false);

    /// <summary>Gets the minimum X.</summary>
    public double MinX { get; }

    /// <summary>Gets the minimum Y.</summary>
    public double MinY { get; }

    /// <summary>Gets the maximum X.</summary>
    public double MaxX { get; }

    /// <summary>Gets the maximum Y.</summary>
    public double MaxY { get; }

    /// <summary>Gets the minimum Z, or positive infinity when no vertical range is present.</summary>
    public double MinZ { get; }

    /// <summary>Gets the maximum Z, or negative infinity when no vertical range is present.</summary>
    public double MaxZ { get; }

    /// <summary>Gets a value indicating whether the extent contains nothing.</summary>
    public bool IsEmpty => !_hasValue;

    /// <summary>Gets a value indicating whether a finite vertical range is present.</summary>
    public bool HasElevation => !IsEmpty && MinZ <= MaxZ && double.IsFinite(MinZ) && double.IsFinite(MaxZ);

    /// <summary>Gets the horizontal span.</summary>
    public double Width => IsEmpty ? 0d : MaxX - MinX;

    /// <summary>Gets the vertical span in plan.</summary>
    public double Height => IsEmpty ? 0d : MaxY - MinY;

    /// <summary>Gets the X of the centre point.</summary>
    public double CentreX => IsEmpty ? 0d : (MinX + MaxX) / 2d;

    /// <summary>Gets the Y of the centre point.</summary>
    public double CentreY => IsEmpty ? 0d : (MinY + MaxY) / 2d;

    /// <summary>Creates a two-dimensional extent, ordering the bounds if they were supplied reversed.</summary>
    /// <param name="minX">One X bound.</param>
    /// <param name="minY">One Y bound.</param>
    /// <param name="maxX">The other X bound.</param>
    /// <param name="maxY">The other Y bound.</param>
    /// <returns>The created extent.</returns>
    public static Extent Create(double minX, double minY, double maxX, double maxY)
    {
        Guard.AgainstNonFinite(minX);
        Guard.AgainstNonFinite(minY);
        Guard.AgainstNonFinite(maxX);
        Guard.AgainstNonFinite(maxY);

        return new Extent(
            Math.Min(minX, maxX),
            Math.Min(minY, maxY),
            Math.Max(minX, maxX),
            Math.Max(minY, maxY),
            double.PositiveInfinity,
            double.NegativeInfinity,
            hasValue: true);
    }

    /// <summary>Creates a three-dimensional extent.</summary>
    /// <param name="minX">One X bound.</param>
    /// <param name="minY">One Y bound.</param>
    /// <param name="minZ">One Z bound.</param>
    /// <param name="maxX">The other X bound.</param>
    /// <param name="maxY">The other Y bound.</param>
    /// <param name="maxZ">The other Z bound.</param>
    /// <returns>The created extent.</returns>
    public static Extent Create(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
    {
        Extent planar = Create(minX, minY, maxX, maxY);

        Guard.AgainstNonFinite(minZ);
        Guard.AgainstNonFinite(maxZ);

        return new Extent(
            planar.MinX,
            planar.MinY,
            planar.MaxX,
            planar.MaxY,
            Math.Min(minZ, maxZ),
            Math.Max(minZ, maxZ),
            hasValue: true);
    }

    /// <summary>Creates the smallest extent containing a single point.</summary>
    /// <param name="x">Point X.</param>
    /// <param name="y">Point Y.</param>
    /// <returns>A degenerate extent at the point.</returns>
    public static Extent FromPoint(double x, double y) => Create(x, y, x, y);

    /// <summary>Returns the smallest extent containing both this and another.</summary>
    /// <param name="other">The extent to combine with.</param>
    /// <returns>The combined extent.</returns>
    public Extent Union(Extent other)
    {
        if (IsEmpty)
        {
            return other;
        }

        if (other.IsEmpty)
        {
            return this;
        }

        double minZ = Math.Min(MinZ, other.MinZ);
        double maxZ = Math.Max(MaxZ, other.MaxZ);

        return new Extent(
            Math.Min(MinX, other.MinX),
            Math.Min(MinY, other.MinY),
            Math.Max(MaxX, other.MaxX),
            Math.Max(MaxY, other.MaxY),
            minZ,
            maxZ,
            hasValue: true);
    }

    /// <summary>Returns this extent grown by a margin on every side.</summary>
    /// <param name="margin">The margin to add. Negative values shrink the extent.</param>
    /// <returns>The expanded extent, or the empty extent if it was already empty.</returns>
    public Extent Expand(double margin)
    {
        if (IsEmpty)
        {
            return this;
        }

        Guard.AgainstNonFinite(margin);

        return Create(MinX - margin, MinY - margin, MaxX + margin, MaxY + margin);
    }

    /// <summary>Determines whether a point lies within the extent, inclusive of its edges.</summary>
    /// <param name="x">Point X.</param>
    /// <param name="y">Point Y.</param>
    /// <returns><see langword="true"/> when the point is inside.</returns>
    public bool Contains(double x, double y) =>
        !IsEmpty && x >= MinX && x <= MaxX && y >= MinY && y <= MaxY;

    /// <summary>Determines whether this extent overlaps another.</summary>
    /// <param name="other">The extent to test.</param>
    /// <returns><see langword="true"/> when the extents share any area.</returns>
    public bool Intersects(Extent other) =>
        !IsEmpty && !other.IsEmpty &&
        MinX <= other.MaxX && MaxX >= other.MinX &&
        MinY <= other.MaxY && MaxY >= other.MinY;

    /// <inheritdoc />
    public override string ToString() => IsEmpty
        ? "Extent(empty)"
        : string.Create(
            CultureInfo.InvariantCulture,
            $"Extent({MinX:0.###}, {MinY:0.###} .. {MaxX:0.###}, {MaxY:0.###})");
}
