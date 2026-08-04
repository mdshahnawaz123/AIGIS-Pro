using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

namespace AiGisConverter.MappingEditor.Application.GisEditing;

public class MeasurementService : IMeasurementService
{
    public double CalculateDistance(Point a, Point b)
    {
        return Math.Sqrt(Math.Pow(b.X - a.X, 2) + Math.Pow(b.Y - a.Y, 2));
    }

    public double CalculateLength(IEnumerable<Point> points)
    {
        var pts = points.ToList();
        double length = 0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            length += CalculateDistance(pts[i], pts[i + 1]);
        }
        return length;
    }

    public double CalculateArea(IEnumerable<Point> points)
    {
        var pts = points.ToList();
        if (pts.Count < 3)
        {
            return 0;
        }
        
        // Ensure closed polygon for Shoelace formula
        if (pts.First() != pts.Last())
        {
            pts.Add(pts.First());
        }

        double area = 0;
        for (int i = 0; i < pts.Count - 1; i++)
        {
            area += (pts[i].X * pts[i + 1].Y) - (pts[i + 1].X * pts[i].Y);
        }
        return Math.Abs(area / 2.0);
    }

    public double CalculateBearing(Point start, Point end)
    {
        // Calculate bearing where North is 0 degrees, clockwise
        double dx = end.X - start.X;
        double dy = end.Y - start.Y; 
        
        // Standard mathematical angle: atan2(dy, dx) gives angle from X axis.
        // For bearing, Y axis is usually North, X axis is East.
        // So bearing = atan2(dx, dy)
        double radians = Math.Atan2(dx, dy);
        double degrees = radians * (180.0 / Math.PI);
        
        if (degrees < 0)
        {
            degrees += 360;
        }
        return degrees;
    }

    public double CalculateAzimuth(Point start, Point end)
    {
        // Azimuth is often treated as equivalent to bearing in planar mapping
        return CalculateBearing(start, end);
    }
}
