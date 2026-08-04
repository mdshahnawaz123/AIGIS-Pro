namespace AiGisConverter.Domain.Enums;

/// <summary>
/// Where a coordinate reference system came from, in descending order of trustworthiness.
/// </summary>
/// <remarks>
/// Recorded on every converted dataset. When a survey lands in the wrong place, the first useful
/// question is always "how did we decide the CRS?", and this answers it without guesswork.
/// </remarks>
public enum CrsDetectionSource
{
    /// <summary>No system was determined.</summary>
    None = 0,

    /// <summary>Stated explicitly by the operator. Overrides everything else.</summary>
    UserSupplied = 1,

    /// <summary>Read from a <c>.prj</c> sidecar next to the source file.</summary>
    PrjSidecar = 2,

    /// <summary>Read from the drawing's own geolocation data.</summary>
    EmbeddedGeoData = 3,

    /// <summary>Read from vendor extended data attached to the drawing.</summary>
    VendorExtendedData = 4,

    /// <summary>Inferred by comparing the coordinate extent against known projected zones.</summary>
    ExtentHeuristic = 5,

    /// <summary>Taken from the application default because nothing else was available.</summary>
    ApplicationDefault = 6,
}
