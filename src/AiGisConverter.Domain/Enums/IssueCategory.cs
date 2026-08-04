namespace AiGisConverter.Domain.Enums;

/// <summary>What kind of thing a validation finding is about.</summary>
public enum IssueCategory
{
    /// <summary>Uncategorised.</summary>
    Unspecified = 0,

    /// <summary>Geometry validity: self-intersection, zero length or area, duplicate vertices.</summary>
    Geometry = 1,

    /// <summary>Attribute quality: nulls, domain violations, field-name or length limits.</summary>
    Attribute = 2,

    /// <summary>Topology: dangles, overlaps, gaps, unclosed boundaries.</summary>
    Topology = 3,

    /// <summary>Coordinate reference system: missing, mismatched, or coordinates out of range.</summary>
    Crs = 4,

    /// <summary>Classification: unclassified layers, or a label below the confidence threshold.</summary>
    Classification = 5,

    /// <summary>Source reading: entities skipped, unsupported primitives, corrupt blocks.</summary>
    SourceIntegrity = 6,

    /// <summary>Export: format limits such as the Shapefile 2 GB or 10-character field-name cap.</summary>
    Export = 7,
}
