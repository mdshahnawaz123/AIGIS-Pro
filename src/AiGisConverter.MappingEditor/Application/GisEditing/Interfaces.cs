using System.Windows;
using NetTopologySuite.Geometries;
using Point = System.Windows.Point;

namespace AiGisConverter.MappingEditor.Application.GisEditing;

public enum SnapMode
{
    None = 0,
    Vertex = 1 << 0,
    Endpoint = 1 << 1,
    Midpoint = 1 << 2,
    Edge = 1 << 3,
    Intersection = 1 << 4,
    Center = 1 << 5,
    Nearest = 1 << 6,
    All = Vertex | Endpoint | Midpoint | Edge | Intersection | Center | Nearest
}

public class SnapResult
{
    public Point Position { get; set; }
    public SnapMode Mode { get; set; }
    public double Distance { get; set; }
}

public interface ISnapService
{
    void BuildIndex(System.Collections.Generic.IEnumerable<AiGisConverter.MappingEditor.Presentation.ViewModels.MapFeatureViewModel> features);
    SnapResult? SnapPoint(Point worldPoint, double tolerance, SnapMode activeModes);
}

public interface IMeasurementService
{
    double CalculateDistance(Point a, Point b);
    double CalculateLength(System.Collections.Generic.IEnumerable<Point> points);
    double CalculateArea(System.Collections.Generic.IEnumerable<Point> points);
    double CalculateBearing(Point start, Point end);
    double CalculateAzimuth(Point start, Point end);
}

public interface ICoordinateFormatter
{
    string FormatCoordinate(Point p);
    string FormatDistance(double distance);
    string FormatArea(double area);
    string FormatBearing(double bearing);
}
