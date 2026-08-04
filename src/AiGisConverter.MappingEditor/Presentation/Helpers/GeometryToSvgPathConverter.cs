using System;
using System.Text;
using NetTopologySuite.Geometries;

namespace AiGisConverter.MappingEditor.Presentation.Helpers;

public static class GeometryToSvgPathConverter
{
    public static string Convert(Geometry geometry, double scale = 1.0, double offsetX = 0, double offsetY = 0)
    {
        if (geometry == null)
        {
            return string.Empty;
        }
        var sb = new StringBuilder();
        
        if (geometry is Point pt)
        {
            var x = (pt.X - offsetX) * scale;
            var y = -(pt.Y - offsetY) * scale;
            sb.Append($"M {x-2},{y} A 2,2 0 1,1 {x+2},{y} A 2,2 0 1,1 {x-2},{y} ");
        }
        else if (geometry is LineString ls)
        {
            AppendCoordinates(sb, ls.Coordinates, scale, offsetX, offsetY);
        }
        else if (geometry is Polygon poly)
        {
            AppendCoordinates(sb, poly.ExteriorRing.Coordinates, scale, offsetX, offsetY);
            foreach (var hole in poly.InteriorRings)
            {
                AppendCoordinates(sb, hole.Coordinates, scale, offsetX, offsetY);
            }
        }
        else if (geometry is MultiLineString mls)
        {
            foreach (var part in mls.Geometries)
            {
                AppendCoordinates(sb, ((LineString)part).Coordinates, scale, offsetX, offsetY);
            }
        }
        else if (geometry is MultiPolygon mp)
        {
            foreach (var part in mp.Geometries)
            {
                var p = (Polygon)part;
                AppendCoordinates(sb, p.ExteriorRing.Coordinates, scale, offsetX, offsetY);
                foreach (var hole in p.InteriorRings)
                {
                    AppendCoordinates(sb, hole.Coordinates, scale, offsetX, offsetY);
                }
            }
        }
        else if (geometry is MultiPoint mpt)
        {
            foreach (var part in mpt.Geometries)
            {
                var pt2 = (Point)part;
                var x = (pt2.X - offsetX) * scale;
                var y = -(pt2.Y - offsetY) * scale;
                sb.Append($"M {x-2},{y} A 2,2 0 1,1 {x+2},{y} A 2,2 0 1,1 {x-2},{y} ");
            }
        }
        
        return sb.ToString();
    }

    private static void AppendCoordinates(StringBuilder sb, Coordinate[] coords, double scale, double offsetX, double offsetY)
    {
        if (coords.Length == 0)
        {
            return;
        }
        
        for (int i = 0; i < coords.Length; i++)
        {
            var c = coords[i];
            var x = (c.X - offsetX) * scale;
            var y = -(c.Y - offsetY) * scale; // Invert Y for screen coordinates
            
            if (i == 0)
            {
                sb.Append($"M {x},{y} ");
            }
            else
            {
                sb.Append($"L {x},{y} ");
            }
        }
    }
}
