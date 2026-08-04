using System.Windows;

namespace AiGisConverter.MappingEditor.Application.GisEditing;

public class CoordinateFormatter : ICoordinateFormatter
{
    public string FormatCoordinate(Point p)
    {
        return $"X: {p.X:F3}, Y: {p.Y:F3}";
    }

    public string FormatDistance(double distance)
    {
        return $"{distance:F3} m";
    }

    public string FormatArea(double area)
    {
        return $"{area:F3} m²";
    }

    public string FormatBearing(double bearing)
    {
        return $"{bearing:F2}°";
    }
}
