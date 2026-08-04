using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using AiGisConverter.MappingEditor.Presentation.ViewModels;
using NetTopologySuite.Geometries;
using NetTopologySuite.Index.Strtree;
using Point = System.Windows.Point;

namespace AiGisConverter.MappingEditor.Application.GisEditing;

public class SnapService : ISnapService
{
    private STRtree<MapFeatureViewModel>? _index;

    public void BuildIndex(IEnumerable<MapFeatureViewModel> features)
    {
        _index = new STRtree<MapFeatureViewModel>();
        foreach (var feature in features)
        {
            if (feature.Result?.Element.Geometry != null)
            {
                _index.Insert(feature.Result.Element.Geometry.EnvelopeInternal, feature);
            }
        }
        _index.Build();
    }

    public SnapResult? SnapPoint(Point worldPoint, double tolerance, SnapMode activeModes)
    {
        if (_index == null || activeModes == SnapMode.None)
        {
            return null;
        }

        var searchEnv = new Envelope(worldPoint.X - tolerance, worldPoint.X + tolerance, worldPoint.Y - tolerance, worldPoint.Y + tolerance);
        var candidates = _index.Query(searchEnv);

        var snapPoint = new NetTopologySuite.Geometries.Point(worldPoint.X, worldPoint.Y);
        SnapResult? bestSnap = null;

        foreach (var candidate in candidates)
        {
            var geom = candidate.Result?.Element.Geometry;
            if (geom == null)
            {
                continue;
            }

            if ((activeModes & SnapMode.Vertex) == SnapMode.Vertex || (activeModes & SnapMode.Endpoint) == SnapMode.Endpoint)
            {
                var coords = geom.Coordinates;
                if (coords.Length > 0)
                {
                    if ((activeModes & SnapMode.Endpoint) == SnapMode.Endpoint)
                    {
                        var dStart = snapPoint.Distance(new NetTopologySuite.Geometries.Point(coords.First()));
                        if (dStart <= tolerance && (bestSnap == null || dStart < bestSnap.Distance))
                        {
                            bestSnap = new SnapResult { Position = new Point(coords.First().X, coords.First().Y), Mode = SnapMode.Endpoint, Distance = dStart };
                        }
                        
                        var dEnd = snapPoint.Distance(new NetTopologySuite.Geometries.Point(coords.Last()));
                        if (dEnd <= tolerance && (bestSnap == null || dEnd < bestSnap.Distance))
                        {
                            bestSnap = new SnapResult { Position = new Point(coords.Last().X, coords.Last().Y), Mode = SnapMode.Endpoint, Distance = dEnd };
                        }
                    }
                    
                    if ((activeModes & SnapMode.Vertex) == SnapMode.Vertex)
                    {
                        foreach (var c in coords)
                        {
                            var d = snapPoint.Distance(new NetTopologySuite.Geometries.Point(c));
                            if (d <= tolerance && (bestSnap == null || d < bestSnap.Distance))
                            {
                                bestSnap = new SnapResult { Position = new Point(c.X, c.Y), Mode = SnapMode.Vertex, Distance = d };
                            }
                        }
                    }
                }
            }

            if ((activeModes & SnapMode.Midpoint) == SnapMode.Midpoint && geom.Coordinates.Length > 1)
            {
                var coords = geom.Coordinates;
                for (int i = 0; i < coords.Length - 1; i++)
                {
                    var midX = (coords[i].X + coords[i+1].X) / 2.0;
                    var midY = (coords[i].Y + coords[i+1].Y) / 2.0;
                    var d = snapPoint.Distance(new NetTopologySuite.Geometries.Point(midX, midY));
                    if (d <= tolerance && (bestSnap == null || d < bestSnap.Distance))
                    {
                        bestSnap = new SnapResult { Position = new Point(midX, midY), Mode = SnapMode.Midpoint, Distance = d };
                    }
                }
            }

            if ((activeModes & SnapMode.Edge) == SnapMode.Edge)
            {
                // Find closest point on edge
                NetTopologySuite.Operation.Distance.DistanceOp distOp = new NetTopologySuite.Operation.Distance.DistanceOp(snapPoint, geom);
                var pts = distOp.NearestPoints();
                if (pts != null && pts.Length == 2)
                {
                    var d = snapPoint.Distance(new NetTopologySuite.Geometries.Point(pts[1]));
                    if (d > 0 && d <= tolerance && (bestSnap == null || d < bestSnap.Distance))
                    {
                        bestSnap = new SnapResult { Position = new Point(pts[1].X, pts[1].Y), Mode = SnapMode.Edge, Distance = d };
                    }
                }
            }
        }

        // Center / Nearest logic could be expanded here. For now Nearest can just be the snapped edge point if it's closest.

        return bestSnap;
    }
}
