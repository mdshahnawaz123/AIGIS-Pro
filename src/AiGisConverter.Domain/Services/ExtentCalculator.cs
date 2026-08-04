using AiGisConverter.Domain.Entities.Gis;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.ValueObjects;
using NetTopologySuite.Geometries;

namespace AiGisConverter.Domain.Services;

/// <summary>
/// Computes bounding boxes over source and converted data.
/// </summary>
/// <remarks>
/// A domain service rather than a method on the entities, because the calculation spans several
/// objects and belongs to none of them. It is deliberately stateless and pure, so it is trivially
/// testable and safe to call from parallel work.
/// </remarks>
public static class ExtentCalculator
{
    /// <summary>Computes the extent of a source document.</summary>
    /// <param name="document">The document to measure.</param>
    /// <returns>The combined extent, or the empty extent when nothing has geometry.</returns>
    public static Extent Measure(SourceDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        Extent extent = Extent.Empty;

        foreach (SourceLayer layer in document.Layers)
        {
            extent = extent.Union(Measure(layer));
        }

        return extent;
    }

    /// <summary>Computes the extent of a source layer.</summary>
    /// <param name="layer">The layer to measure.</param>
    /// <returns>The combined extent.</returns>
    public static Extent Measure(SourceLayer layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        Extent extent = Extent.Empty;

        foreach (SourceElement element in layer.Elements)
        {
            extent = extent.Union(Measure(element.Geometry));
        }

        return extent;
    }

    /// <summary>Computes the extent of a set of datasets.</summary>
    /// <param name="datasets">The datasets to measure.</param>
    /// <returns>The combined extent.</returns>
    public static Extent Measure(IEnumerable<GisDataset> datasets)
    {
        ArgumentNullException.ThrowIfNull(datasets);

        Extent extent = Extent.Empty;

        foreach (GisDataset dataset in datasets)
        {
            extent = extent.Union(dataset.Extent);
        }

        return extent;
    }

    /// <summary>Computes the extent of a geometry.</summary>
    /// <param name="geometry">The geometry to measure. May be null.</param>
    /// <returns>The extent, or the empty extent when the geometry is null or empty.</returns>
    public static Extent Measure(Geometry? geometry)
    {
        if (geometry is null || geometry.IsEmpty)
        {
            return Extent.Empty;
        }

        Envelope envelope = geometry.EnvelopeInternal;

        return Extent.Create(envelope.MinX, envelope.MinY, envelope.MaxX, envelope.MaxY);
    }
}
