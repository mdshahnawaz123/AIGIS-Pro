using System.Collections.Generic;
using System.Threading.Tasks;
using AiGisConverter.MappingEditor.Presentation.ViewModels;

namespace AiGisConverter.MappingEditor.Presentation.Abstractions;

/// <summary>
/// Abstraction for rendering map features, allowing future implementation
/// via WPF DrawingVisual, SkiaSharp, Direct2D, or OpenGL.
/// </summary>
public interface IMapRenderer
{
    /// <summary>
    /// Binds the renderer to a collection of features.
    /// </summary>
    void SetFeatures(IEnumerable<MapFeatureViewModel> features);
    
    /// <summary>
    /// Forces a redraw of all layers.
    /// </summary>
    Task RenderAsync();
    
    /// <summary>
    /// Invalidates and redraws only the selection and hover layers.
    /// </summary>
    void InvalidateDynamicLayers();
    
    /// <summary>
    /// Sets the viewport scaling and translation.
    /// </summary>
    void UpdateViewport(double scale, double offsetX, double offsetY);

    /// <summary>
    /// Hit tests the current mouse position against rendered geometries.
    /// </summary>
    MapFeatureViewModel? HitTest(double x, double y);

    /// <summary>
    /// Selects multiple features within a bounding box.
    /// </summary>
    IEnumerable<MapFeatureViewModel> SelectInBox(double minX, double minY, double maxX, double maxY);
}
