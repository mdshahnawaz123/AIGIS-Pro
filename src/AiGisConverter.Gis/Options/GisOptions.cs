using System.ComponentModel.DataAnnotations;

namespace AiGisConverter.Gis.Options;

/// <summary>
/// GIS engine configuration, bound from the <c>Gis</c> section.
/// </summary>
/// <remarks>
/// Every threshold the engine applies is here. Nothing in the geometry, validation or export code
/// carries a literal tolerance: a number that decides whether a surveyed boundary is valid belongs
/// in configuration where it can be seen and argued with, not buried in a comparison.
/// </remarks>
public sealed class GisOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Gis";

    /// <summary>Gets or sets the profile applied when a job names none.</summary>
    public string DefaultProfile { get; set; } = "generic-geojson";

    /// <summary>Gets the folders scanned for user-supplied profile JSON.</summary>
    public IList<string> ProfileSearchPaths { get; } =
    [
        "Profiles",
        "%LOCALAPPDATA%\\AiGisConverter\\Profiles",
    ];

    /// <summary>Gets the geometry settings.</summary>
    public GeometryOptions Geometry { get; } = new();

    /// <summary>Gets the streaming settings.</summary>
    public StreamingOptions Streaming { get; } = new();

    /// <summary>Gets the coordinate system settings.</summary>
    public CrsOptions Crs { get; } = new();

    /// <summary>Gets the output settings.</summary>
    public ExportOptions Export { get; } = new();
}

/// <summary>Output settings.</summary>
public sealed class ExportOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether GeoJSON omits properties that have no value.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The attribute schema is uniform across a dataset, because a shapefile or a database table
    /// requires it. GeoJSON does not: RFC 7946 puts no constraint on features sharing property
    /// names, and every consumer that reads it - QGIS, ArcGIS, ogr2ogr, geopandas - unions the keys
    /// it finds and fills the gaps itself. Writing the nulls out is therefore optional, and on a
    /// wide schema it is most of the file.
    /// </para>
    /// <para>
    /// Measured on a real BIM export: 22,946 features over an 85-column schema, of which 52 columns
    /// came from nine elements. Those nine widened every other feature by 52 nulls, and the nulls
    /// were 29.2 MB of a 67.4 MB file.
    /// </para>
    /// <para>
    /// Left true. Set false to restore the previous output byte for byte if a downstream consumer
    /// infers its columns from one feature rather than from the collection.
    /// </para>
    /// </remarks>
    public bool OmitNullGeoJsonProperties { get; set; } = true;
}

/// <summary>Geometry handling thresholds.</summary>
public sealed class GeometryOptions
{
    /// <summary>Gets or sets the maximum deviation, in target units, when tessellating a curve.</summary>
    [Range(1e-9d, 1e6d)]
    public double ChordTolerance { get; set; } = 0.01d;

    /// <summary>Gets or sets the distance below which two vertices are the same vertex.</summary>
    [Range(0d, 1e6d)]
    public double VertexTolerance { get; set; } = 1e-8d;

    /// <summary>Gets or sets the length below which a line is treated as having none.</summary>
    [Range(0d, 1e6d)]
    public double MinimumLineLength { get; set; } = 1e-6d;

    /// <summary>Gets or sets the area below which a polygon is treated as having none.</summary>
    [Range(0d, 1e12d)]
    public double MinimumPolygonArea { get; set; } = 1e-9d;

    /// <summary>Gets or sets the coordinate grid the output is snapped to. Zero disables snapping.</summary>
    /// <remarks>
    /// Expressed as a scale factor in the NetTopologySuite sense: 1000 keeps three decimal places.
    /// Snapping is what makes repeated conversions of the same drawing produce byte-identical
    /// output, which is the difference between a diffable deliverable and an unreviewable one.
    /// </remarks>
    [Range(0d, 1e12d)]
    public double PrecisionScale { get; set; }

    /// <summary>Gets or sets a value indicating whether invalid geometry is repaired rather than rejected.</summary>
    public bool RepairInvalidGeometry { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether geometry that cannot be repaired is dropped.</summary>
    /// <remarks>
    /// When false the feature is exported with null geometry and a critical finding, so the
    /// attribute row survives for the operator to fix. Silently losing a parcel is worse than
    /// exporting one that is visibly wrong.
    /// </remarks>
    public bool DropIrreparableGeometry { get; set; }

    /// <summary>Gets or sets a value indicating whether ring orientation is normalised on export.</summary>
    public bool NormaliseRingOrientation { get; set; } = true;

    /// <summary>Gets or sets the Douglas-Peucker distance used when a profile requests simplification.</summary>
    [Range(0d, 1e6d)]
    public double SimplificationTolerance { get; set; }
}

/// <summary>Streaming and throughput settings.</summary>
public sealed class StreamingOptions
{
    /// <summary>Gets or sets how many features are written between flushes.</summary>
    [Range(1, 1_000_000)]
    public int FlushInterval { get; set; } = 5_000;

    /// <summary>Gets or sets how many features are processed between progress reports.</summary>
    [Range(1, 1_000_000)]
    public int ProgressInterval { get; set; } = 10_000;

    /// <summary>Gets or sets the write buffer size in bytes.</summary>
    [Range(4_096, 16_777_216)]
    public int BufferSize { get; set; } = 262_144;

    /// <summary>
    /// Gets or sets how many features the validation stage batches before handing them on.
    /// </summary>
    /// <remarks>
    /// Validation is the only stage worth parallelising: it is CPU-bound and per-feature, whereas
    /// writing is inherently serial. The batch exists so parallel work has enough to amortise its
    /// own coordination cost.
    /// </remarks>
    [Range(1, 100_000)]
    public int ValidationBatchSize { get; set; } = 1_000;

    /// <summary>Gets or sets the degree of parallelism for validation. Zero means processor count.</summary>
    [Range(0, 256)]
    public int MaxDegreeOfParallelism { get; set; }

    /// <summary>Gets or sets the number of feature failures tolerated before a run is abandoned.</summary>
    /// <remarks>
    /// A drawing where every feature fails is a different problem from one with a handful of bad
    /// polygons, and should stop early rather than spend an hour producing an empty file.
    /// </remarks>
    [Range(0, int.MaxValue)]
    public int MaxConsecutiveFailures { get; set; } = 1_000;
}

/// <summary>Coordinate system settings.</summary>
public sealed class CrsOptions
{
    /// <summary>Gets or sets the folder holding the PROJ database. Empty uses the bundled one.</summary>
    public string ProjDataPath { get; set; } = string.Empty;

    /// <summary>Gets or sets the folder holding GDAL's support files. Empty uses the bundled ones.</summary>
    public string GdalDataPath { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether transformations are cached by system pair.</summary>
    public bool CacheTransformations { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether axis order follows the authority's definition.
    /// </summary>
    /// <remarks>
    /// EPSG defines 4326 as latitude-then-longitude, while every GeoJSON file in existence is
    /// longitude-then-latitude. Left false, which forces the traditional x/y order and is what
    /// stops output landing in the Indian Ocean.
    /// </remarks>
    public bool UseAuthorityAxisOrder { get; set; }
}
