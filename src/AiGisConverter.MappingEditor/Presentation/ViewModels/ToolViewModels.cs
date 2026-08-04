using System;
using System.Collections.ObjectModel;
using System.Windows;
using AiGisConverter.MappingEditor.Application.GisEditing;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiGisConverter.MappingEditor.Presentation.ViewModels;

public enum MapToolMode
{
    Select,
    Pan,
    MeasureDistance,
    MeasureArea,
    CoordinatePicker
}

public partial class MeasurementViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _name = "Measurement";

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private string _color = "Magenta";

    [ObservableProperty]
    private ObservableCollection<Point> _points = new();

    [ObservableProperty]
    private double _distance;

    [ObservableProperty]
    private double _area;

    [ObservableProperty]
    private double _bearing;

    [ObservableProperty]
    private bool _isPolygon;
}

public partial class SnappingSettingsViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _isSnappingEnabled = true;

    [ObservableProperty]
    private double _snapTolerance = 10.0; // In screen pixels

    [ObservableProperty]
    private bool _snapVertex = true;

    [ObservableProperty]
    private bool _snapEndpoint = true;

    [ObservableProperty]
    private bool _snapMidpoint = false;

    [ObservableProperty]
    private bool _snapEdge = false;

    [ObservableProperty]
    private bool _snapIntersection = false;

    [ObservableProperty]
    private bool _snapCenter = false;

    [ObservableProperty]
    private bool _snapNearest = false;

    public SnapMode GetActiveModes()
    {
        if (!IsSnappingEnabled) 
        {
            return SnapMode.None;
        }

        SnapMode mode = SnapMode.None;
        if (SnapVertex) 
        {
            mode |= SnapMode.Vertex;
        }
        if (SnapEndpoint) 
        {
            mode |= SnapMode.Endpoint;
        }
        if (SnapMidpoint) 
        {
            mode |= SnapMode.Midpoint;
        }
        if (SnapEdge) 
        {
            mode |= SnapMode.Edge;
        }
        if (SnapIntersection) 
        {
            mode |= SnapMode.Intersection;
        }
        if (SnapCenter) 
        {
            mode |= SnapMode.Center;
        }
        if (SnapNearest) 
        {
            mode |= SnapMode.Nearest;
        }
        return mode;
    }
}
