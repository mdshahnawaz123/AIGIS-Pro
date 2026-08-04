using System.ComponentModel.DataAnnotations;
using AiGisConverter.Domain.Enums;

namespace AiGisConverter.Cad.Options;

/// <summary>
/// Settings governing how CAD sources are read, bound from the <c>Cad</c> section.
/// </summary>
public sealed class CadOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Cad";

    /// <summary>Gets the curve tessellation settings.</summary>
    public TessellationOptions Tessellation { get; } = new();

    /// <summary>Gets or sets a value indicating whether layers switched off in the drawing are read.</summary>
    public bool IncludeInvisibleLayers { get; set; }

    /// <summary>Gets or sets a value indicating whether frozen layers are read.</summary>
    public bool IncludeFrozenLayers { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether block references are expanded into their constituent
    /// entities.
    /// </summary>
    /// <remarks>
    /// When false a block becomes a single point at its insertion point, which is usually what a
    /// GIS wants for symbols such as manholes or trees. When true the block's geometry is emitted
    /// instead, which is what a GIS wants for blocks used as drafting containers.
    /// </remarks>
    public bool ExplodeBlocks { get; set; }

    /// <summary>Gets or sets how deep nested block references are followed.</summary>
    /// <remarks>
    /// Bounded because a malformed drawing can contain a block that references itself, and an
    /// unbounded walk over one is a stack overflow rather than an error message.
    /// </remarks>
    [Range(1, 16)]
    public int MaxBlockNestingDepth { get; set; } = 8;

    /// <summary>Gets or sets a value indicating whether block attribute values are read.</summary>
    public bool ReadBlockAttributes { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether text entities are emitted as features.</summary>
    public bool ReadText { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether hatch entities are emitted as polygons.</summary>
    public bool ReadHatches { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether dimensions are emitted.</summary>
    /// <remarks>Off by default: dimensions are drafting annotation and rarely wanted in a GIS.</remarks>
    public bool ReadDimensions { get; set; }

    /// <summary>Gets or sets the units to assume when the drawing header declares none.</summary>
    public LinearUnit AssumedUnits { get; set; } = LinearUnit.Unknown;

    /// <summary>Gets or sets a value indicating whether a <c>.prj</c> sidecar is consulted for the CRS.</summary>
    public bool ReadCrsSidecar { get; set; } = true;

    /// <summary>Gets or sets the maximum elements read before the reader gives up.</summary>
    /// <remarks>
    /// A guard against a drawing that is not what the user thought it was. Zero disables the limit.
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int MaxElements { get; set; }
}

/// <summary>
/// Controls how curves are approximated by straight segments.
/// </summary>
/// <remarks>
/// <para>
/// Tessellation is driven by a chord tolerance rather than a fixed segment count, because a fixed
/// count is wrong at both ends of the scale: thirty-two segments is wasteful on a 50 mm fillet and
/// visibly polygonal on a 500 m highway curve. A tolerance expresses the thing the surveyor
/// actually cares about &#8212; how far the approximation may stray from the true curve.
/// </para>
/// <para>
/// The tolerance is in drawing units. A drawing in millimetres and one in metres need different
/// numbers, which is why unit detection runs before conversion.
/// </para>
/// </remarks>
public sealed class TessellationOptions
{
    /// <summary>Gets or sets the maximum distance, in drawing units, between the chord and the true curve.</summary>
    [Range(1e-9d, 1e6d)]
    public double ChordTolerance { get; set; } = 0.01d;

    /// <summary>Gets or sets the fewest segments any curve is split into.</summary>
    [Range(2, 1024)]
    public int MinimumSegments { get; set; } = 4;

    /// <summary>Gets or sets the most segments any curve is split into.</summary>
    [Range(4, 100_000)]
    public int MaximumSegments { get; set; } = 512;

    /// <summary>Gets or sets the number of segments used per spline control interval.</summary>
    [Range(2, 512)]
    public int SegmentsPerSplineSpan { get; set; } = 16;
}
