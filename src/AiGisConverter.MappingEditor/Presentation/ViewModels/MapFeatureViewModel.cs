using CommunityToolkit.Mvvm.ComponentModel;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.MappingEditor.Business;

namespace AiGisConverter.MappingEditor.Presentation.ViewModels;

public partial class MapFeatureViewModel : ObservableObject
{
    [ObservableProperty]
    private string _pathData = string.Empty;

    [ObservableProperty]
    private string _stroke = "White";

    [ObservableProperty]
    private double _strokeThickness = 1.0;

    [ObservableProperty]
    private string _fill = "Transparent";

    [ObservableProperty]
    private string _tooltipText = string.Empty;

    [ObservableProperty]
    private bool _isSelected;

    public SimulationResult? Result { get; set; }

    /// <summary>Gets or sets the CAD layer the feature came from, for layer and rule matching.</summary>
    public string SourceLayer { get; set; } = string.Empty;

    /// <summary>Gets or sets the native CAD type, for example <c>LWPOLYLINE</c>.</summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>Gets or sets the assigned GIS feature class, for rule matching.</summary>
    public string FeatureClassName { get; set; } = string.Empty;

    /// <summary>Gets or sets the geometry bounds in world coordinates, for zoom-to-selection.</summary>
    public System.Windows.Rect Bounds { get; set; } = System.Windows.Rect.Empty;

    // --- Attribute table columns -------------------------------------------------------------
    // Populated from the converted feature so the table shows the drawing's own values rather
    // than a second, derived copy of them.

    /// <summary>Gets or sets the CAD handle, the drawing's own identifier for the entity.</summary>
    public string Handle { get; set; } = string.Empty;

    /// <summary>Gets or sets the geometry family, for example <c>Polygon</c>.</summary>
    public string GeometryType { get; set; } = string.Empty;

    /// <summary>Gets or sets the length in source units, when the entity has one.</summary>
    public double? Length { get; set; }

    /// <summary>Gets or sets the area in source units, when the entity encloses one.</summary>
    public double? Area { get; set; }

    /// <summary>Gets or sets the classification label the AI layer assigned, when any.</summary>
    public string Classification { get; set; } = string.Empty;

    /// <summary>Gets or sets the classification confidence, between 0 and 1.</summary>
    public double? Confidence { get; set; }

    /// <summary>Gets or sets the name of the rule that classified the feature, when any.</summary>
    public string RuleName { get; set; } = string.Empty;

    /// <summary>Gets or sets the CAD colour index, when the entity declares one.</summary>
    public string Color { get; set; } = string.Empty;

    /// <summary>Gets the status shown in the table: classified, or needing attention.</summary>
    public string Status =>
        string.IsNullOrWhiteSpace(Classification) || Classification.Equals("Unclassified", System.StringComparison.OrdinalIgnoreCase)
            ? "Unclassified"
            : "Classified";

    /// <summary>Gets the searchable text for the table's filter box.</summary>
    /// <remarks>Concatenated once per feature so filtering does not re-read every column per keystroke.</remarks>
    public string SearchText =>
        $"{Handle} {SourceLayer} {EntityType} {GeometryType} {FeatureClassName} {Classification} {RuleName} {Status}";
}
