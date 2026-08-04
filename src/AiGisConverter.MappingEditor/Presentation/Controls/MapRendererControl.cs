using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using AiGisConverter.MappingEditor.Presentation.Abstractions;
using AiGisConverter.MappingEditor.Presentation.ViewModels;
using AiGisConverter.MappingEditor.Application.GisEditing;

namespace AiGisConverter.MappingEditor.Presentation.Controls;

public class MapRendererControl : FrameworkElement, IMapRenderer
{
    private readonly VisualCollection _visuals;
    private DispatcherTimer? _flashTimer;
    private readonly DrawingVisual _baseVisual;
    private readonly DrawingVisual _selectionVisual;
    private readonly DrawingVisual _hoverVisual;
    private readonly DrawingVisual _labelsVisual;
    private readonly DrawingVisual _measurementVisual;
    private readonly DrawingVisual _snapMarkerVisual;

    private readonly ISnapService _snapService = new SnapService();
    private readonly IMeasurementService _measurementService = new MeasurementService();
    private readonly ICoordinateFormatter _formatter = new CoordinateFormatter();

    private readonly List<Point> _currentMeasurementPoints = new();
    private SnapResult? _currentSnap;

    private IEnumerable<MapFeatureViewModel>? _features;
    private MapFeatureViewModel? _hoveredFeature;
    private double _scale = 1.0;
    private double _offsetX;
    private double _offsetY;
    
    private Point _lastPanPosition;
    private bool _isPanning;
    private bool _isSelecting;

    public static readonly DependencyProperty ItemsSourceProperty =
        DependencyProperty.Register(
            nameof(ItemsSource),
            typeof(IEnumerable<MapFeatureViewModel>),
            typeof(MapRendererControl),
            new PropertyMetadata(null, OnItemsSourceChanged));

    public IEnumerable<MapFeatureViewModel>? ItemsSource
    {
        get => (IEnumerable<MapFeatureViewModel>?)GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    public MapRendererControl()
    {
        _visuals = new VisualCollection(this);
        
        _baseVisual = new DrawingVisual();
        _selectionVisual = new DrawingVisual();
        _hoverVisual = new DrawingVisual();
        _labelsVisual = new DrawingVisual();
        _measurementVisual = new DrawingVisual();
        _snapMarkerVisual = new DrawingVisual();

        _visuals.Add(_baseVisual);
        _visuals.Add(_selectionVisual);
        _visuals.Add(_hoverVisual);
        _visuals.Add(_labelsVisual);
        _visuals.Add(_measurementVisual);
        _visuals.Add(_snapMarkerVisual);

        ClipToBounds = true;
        Focusable = true;
    }

    private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is MapRendererControl control)
        {
            if (e.OldValue is INotifyCollectionChanged oldCollection)
            {
                oldCollection.CollectionChanged -= control.OnCollectionChanged;
            }

            control.SetFeatures(e.NewValue as IEnumerable<MapFeatureViewModel> ?? Array.Empty<MapFeatureViewModel>());

            if (e.NewValue is INotifyCollectionChanged newCollection)
            {
                newCollection.CollectionChanged += control.OnCollectionChanged;
            }
        }
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        _ = RenderAsync();
    }

    protected override int VisualChildrenCount => _visuals.Count;

    protected override Visual GetVisualChild(int index) => _visuals[index];

    public void SetFeatures(IEnumerable<MapFeatureViewModel> features)
    {
        _features = features;
        
        // Build snap index in background
        Task.Run(() => _snapService.BuildIndex(features));

        _ = RenderAsync();
    }

    public async Task RenderAsync()
    {
        if (_features == null || !_features.Any())
        {
            using (var dc = _baseVisual.RenderOpen()) { }
            using (var dc = _selectionVisual.RenderOpen()) { }
            using (var dc = _hoverVisual.RenderOpen()) { }
            using (var dc = _labelsVisual.RenderOpen()) { }
            return;
        }

        // 1. Prepare rendering data asynchronously
        var featuresSnapshot = _features.ToList();
        
        // Culling & LOD metrics
        double viewportMinX = -_offsetX / _scale;
        double viewportMinY = -_offsetY / _scale;
        double viewportMaxX = (ActualWidth - _offsetX) / _scale;
        double viewportMaxY = (ActualHeight - _offsetY) / _scale;

        // Ensure valid viewport bounds for initial render
        if (viewportMaxX <= viewportMinX) 
        {
            viewportMaxX = viewportMinX + 1000;
        }
        if (viewportMaxY <= viewportMinY) 
        {
            viewportMaxY = viewportMinY + 1000;
        }

        await Task.Run(() =>
        {
            // In a full implementation, we parse geometry and generate StreamGeometry
            // For now, WPF handles PathData parsing well enough if we use Geometry.Parse on UI thread,
            // but we can parse it here if we freeze it.
        });

        // 2. Swap visuals on UI thread
        DrawBaseLayers(featuresSnapshot, viewportMinX, viewportMinY, viewportMaxX, viewportMaxY);
        InvalidateDynamicLayers();
    }

    private void DrawBaseLayers(List<MapFeatureViewModel> features, double minX, double minY, double maxX, double maxY)
    {
        using var dc = _baseVisual.RenderOpen();
        
        // Apply view transform
        var transform = new MatrixTransform(_scale, 0, 0, _scale, _offsetX, _offsetY);
        dc.PushTransform(transform);

        foreach (var feature in features)
        {
            if (string.IsNullOrEmpty(feature.PathData))
            {
                continue;
            }
            
            try
            {
                var geometry = Geometry.Parse(feature.PathData);
                geometry.Freeze(); // Optimize

                // Viewport culling (basic bounds check)
                if (geometry.Bounds.Right < minX || geometry.Bounds.Left > maxX ||
                    geometry.Bounds.Bottom < minY || geometry.Bounds.Top > maxY)
                {
                    continue; // Skip off-screen
                }

                // Level of detail: Skip tiny polygons
                if (_scale < 0.5 && geometry.Bounds.Width * _scale < 2 && geometry.Bounds.Height * _scale < 2)
                {
                    continue; 
                }

                var strokeBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(feature.Stroke));
                strokeBrush.Freeze();
                
                var fillBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(feature.Fill));
                fillBrush.Freeze();
                
                var pen = new Pen(strokeBrush, feature.StrokeThickness / _scale);
                pen.Freeze();

                dc.DrawGeometry(fillBrush, pen, geometry);
            }
            catch
            {
                // Ignore parse errors for malformed paths
            }
        }
        
        dc.Pop();
    }

    public void InvalidateDynamicLayers()
    {
        if (_features == null)
        {
            return;
        }

        using var selectionDc = _selectionVisual.RenderOpen();
        using var hoverDc = _hoverVisual.RenderOpen();
        
        var transform = new MatrixTransform(_scale, 0, 0, _scale, _offsetX, _offsetY);
        selectionDc.PushTransform(transform);
        hoverDc.PushTransform(transform);

        var selectionPen = new Pen(Brushes.Yellow, 3.0 / _scale);
        selectionPen.Freeze();

        var hoverPen = new Pen(Brushes.Orange, 3.0 / _scale);
        hoverPen.Freeze();

        foreach (var feature in _features)
        {
            if (!feature.IsSelected && feature != _hoveredFeature)
            {
                continue;
            }
            if (string.IsNullOrEmpty(feature.PathData))
            {
                continue;
            }

            try
            {
                var geometry = Geometry.Parse(feature.PathData);
                geometry.Freeze();

                if (feature.IsSelected)
                {
                    selectionDc.DrawGeometry(null, selectionPen, geometry);
                }

                if (feature == _hoveredFeature)
                {
                    hoverDc.DrawGeometry(null, hoverPen, geometry);
                }
            }
            catch { }
        }

        selectionDc.Pop();
        hoverDc.Pop();
    }

    private void DrawHoverLayer()
    {
        using var dc = _hoverVisual.RenderOpen();
        if (_hoveredFeature != null)
        {
            // Render hover highlight
            try
            {
                var geometry = Geometry.Parse(_hoveredFeature.PathData);
                var transform = new MatrixTransform(_scale, 0, 0, -_scale, _offsetX, _offsetY);
                geometry.Transform = transform;

                var pen = new Pen(Brushes.Cyan, 3.0);
                pen.Freeze();
                dc.DrawGeometry(null, pen, geometry);
            }
            catch { }
        }
    }

    private void DrawSnapMarker()
    {
        using var dc = _snapMarkerVisual.RenderOpen();
        if (_currentSnap == null)
        {
            return;
        }

        var screenPt = WorldToScreen(_currentSnap.Position);
        var pen = new Pen(Brushes.Magenta, 2.0);
        pen.Freeze();
        var brush = Brushes.Transparent;

        double size = 8;
        
        switch (_currentSnap.Mode)
        {
            case SnapMode.Vertex:
                dc.DrawRectangle(brush, pen, new Rect(screenPt.X - size/2, screenPt.Y - size/2, size, size));
                break;
            case SnapMode.Endpoint:
                dc.DrawEllipse(Brushes.Magenta, pen, screenPt, size/2, size/2);
                break;
            case SnapMode.Midpoint:
                var p1 = new Point(screenPt.X, screenPt.Y - size/2);
                var p2 = new Point(screenPt.X - size/2, screenPt.Y + size/2);
                var p3 = new Point(screenPt.X + size/2, screenPt.Y + size/2);
                var tri = new StreamGeometry();
                using (var ctx = tri.Open())
                {
                    ctx.BeginFigure(p1, true, true);
                    ctx.LineTo(p2, true, false);
                    ctx.LineTo(p3, true, false);
                }
                tri.Freeze();
                dc.DrawGeometry(brush, pen, tri);
                break;
            case SnapMode.Edge:
                var d1 = new Point(screenPt.X, screenPt.Y - size/2);
                var d2 = new Point(screenPt.X + size/2, screenPt.Y);
                var d3 = new Point(screenPt.X, screenPt.Y + size/2);
                var d4 = new Point(screenPt.X - size/2, screenPt.Y);
                var diamond = new StreamGeometry();
                using (var ctx = diamond.Open())
                {
                    ctx.BeginFigure(d1, true, true);
                    ctx.LineTo(d2, true, false);
                    ctx.LineTo(d3, true, false);
                    ctx.LineTo(d4, true, false);
                }
                diamond.Freeze();
                dc.DrawGeometry(brush, pen, diamond);
                break;
            case SnapMode.Intersection:
                dc.DrawLine(pen, new Point(screenPt.X - size/2, screenPt.Y - size/2), new Point(screenPt.X + size/2, screenPt.Y + size/2));
                dc.DrawLine(pen, new Point(screenPt.X + size/2, screenPt.Y - size/2), new Point(screenPt.X - size/2, screenPt.Y + size/2));
                break;
            case SnapMode.Nearest:
                dc.DrawEllipse(brush, pen, screenPt, size/3, size/3);
                break;
            case SnapMode.Center:
                dc.DrawLine(pen, new Point(screenPt.X, screenPt.Y - size/2), new Point(screenPt.X, screenPt.Y + size/2));
                dc.DrawLine(pen, new Point(screenPt.X - size/2, screenPt.Y), new Point(screenPt.X + size/2, screenPt.Y));
                dc.DrawEllipse(brush, pen, screenPt, size/4, size/4);
                break;
        }
    }

    private void DrawActiveMeasurement(Point mousePos)
    {
        using var dc = _measurementVisual.RenderOpen();
        if (_currentMeasurementPoints.Count == 0)
        {
            return;
        }

        var vm = DataContext as MappingEditorViewModel;
        if (vm == null)
        {
            return;
        }

        var currentWorld = _currentSnap != null ? _currentSnap.Position : ScreenToWorld(mousePos);

        var pts = new List<Point>(_currentMeasurementPoints);
        pts.Add(currentWorld);

        var screenPts = pts.Select(WorldToScreen).ToList();

        var pen = new Pen(Brushes.Magenta, 2.0) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        var brush = Brushes.Magenta.Clone();
        brush.Opacity = 0.2;
        brush.Freeze();

        var geom = new StreamGeometry();
        using (var ctx = geom.Open())
        {
            ctx.BeginFigure(screenPts[0], vm.CurrentToolMode == MapToolMode.MeasureArea, vm.CurrentToolMode == MapToolMode.MeasureArea);
            for (int i = 1; i < screenPts.Count; i++)
            {
                ctx.LineTo(screenPts[i], true, false);
            }
        }
        geom.Freeze();
        
        dc.DrawGeometry(vm.CurrentToolMode == MapToolMode.MeasureArea ? brush : null, pen, geom);

        // Draw label
        var lastPt = screenPts.Last();
        string text = "";
        if (vm.CurrentToolMode == MapToolMode.MeasureDistance)
        {
            if (pts.Count == 2)
            {
                text = _formatter.FormatDistance(_measurementService.CalculateDistance(pts[0], pts[1]));
                text += $"\nBearing: {_formatter.FormatBearing(_measurementService.CalculateBearing(pts[0], pts[1]))}";
            }
            else
            {
                text = _formatter.FormatDistance(_measurementService.CalculateLength(pts));
            }
        }
        else if (vm.CurrentToolMode == MapToolMode.MeasureArea && pts.Count >= 3)
        {
            text = _formatter.FormatArea(_measurementService.CalculateArea(pts));
        }

        if (!string.IsNullOrEmpty(text))
        {
            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                14,
                Brushes.White,
                1.25);
            dc.DrawRectangle(Brushes.Black, null, new Rect(lastPt.X + 10, lastPt.Y + 10, formatted.Width + 10, formatted.Height + 10));
            dc.DrawText(formatted, new Point(lastPt.X + 15, lastPt.Y + 15));
        }
    }

    public void UpdateViewport(double scale, double offsetX, double offsetY)
    {
        _scale = scale;
        _offsetX = offsetX;
        _offsetY = offsetY;

        // Re-render entirely for culling and scale adjustments
        _ = RenderAsync();
    }

    /// <summary>
    /// Frames every loaded feature, centred, with a small margin.
    /// </summary>
    /// <remarks>
    /// The union of the rendered geometry is measured rather than assuming the data sits near the
    /// origin: a drawing in a projected grid is hundreds of thousands of units from it, and the
    /// old "reset to scale 1" behaviour left such a drawing off-screen with no way back.
    /// </remarks>
    /// <returns><see langword="true"/> when there was geometry to frame.</returns>
    public bool ZoomToData()
    {
        Rect bounds = DataBounds();

        if (bounds.IsEmpty || bounds.Width <= 0d && bounds.Height <= 0d)
        {
            return false;
        }

        double viewWidth = ActualWidth > 0d ? ActualWidth : 800d;
        double viewHeight = ActualHeight > 0d ? ActualHeight : 600d;

        // 8% margin so edge features are not flush against the border.
        double scaleX = bounds.Width > 0d ? viewWidth / bounds.Width : double.MaxValue;
        double scaleY = bounds.Height > 0d ? viewHeight / bounds.Height : double.MaxValue;
        double scale = Math.Min(scaleX, scaleY) * 0.92d;

        if (!double.IsFinite(scale) || scale <= 0d)
        {
            scale = 1d;
        }

        double centreX = bounds.X + (bounds.Width / 2d);
        double centreY = bounds.Y + (bounds.Height / 2d);

        UpdateViewport(
            scale,
            (viewWidth / 2d) - (centreX * scale),
            (viewHeight / 2d) - (centreY * scale));

        return true;
    }

    /// <summary>
    /// Briefly pulses the selection highlight so the eye can find it on a dense drawing.
    /// </summary>
    /// <remarks>
    /// Implemented by animating the existing selection layer's opacity rather than adding another
    /// visual: flashing is a way of drawing attention to the selection, not a second kind of
    /// selection, and a separate layer would have to be kept in step with this one forever.
    /// </remarks>
    /// <param name="pulses">How many times to pulse.</param>
    public void FlashSelection(int pulses = 3)
    {
        // A DrawingVisual is not IAnimatable, so this is driven by a timer rather than a
        // storyboard. Any flash already running is replaced, so repeated clicks do not stack
        // timers that fight over the same opacity.
        _flashTimer?.Stop();

        int remaining = Math.Max(1, pulses) * 2;
        _flashTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(160),
        };

        _flashTimer.Tick += (_, _) =>
        {
            _selectionVisual.Opacity = _selectionVisual.Opacity > 0.5d ? 0.15d : 1d;

            if (--remaining <= 0)
            {
                _flashTimer!.Stop();
                _selectionVisual.Opacity = 1d;
            }
        };

        _flashTimer.Start();
    }

    /// <summary>Frames only the currently selected features.</summary>
    /// <returns><see langword="true"/> when something was selected to frame.</returns>
    public bool ZoomToSelection()
    {
        Rect bounds = DataBounds(selectedOnly: true);

        if (bounds.IsEmpty)
        {
            return false;
        }

        double viewWidth = ActualWidth > 0d ? ActualWidth : 800d;
        double viewHeight = ActualHeight > 0d ? ActualHeight : 600d;

        // A single point has no extent; give it a working window rather than dividing by zero.
        if (bounds.Width <= 0d || bounds.Height <= 0d)
        {
            bounds.Inflate(Math.Max(bounds.Width, 10d), Math.Max(bounds.Height, 10d));
        }

        double scale = Math.Min(viewWidth / bounds.Width, viewHeight / bounds.Height) * 0.75d;

        if (!double.IsFinite(scale) || scale <= 0d)
        {
            scale = 1d;
        }

        double centreX = bounds.X + (bounds.Width / 2d);
        double centreY = bounds.Y + (bounds.Height / 2d);

        UpdateViewport(
            scale,
            (viewWidth / 2d) - (centreX * scale),
            (viewHeight / 2d) - (centreY * scale));

        return true;
    }

    /// <summary>Gets the union of every loaded feature's bounds, in world coordinates.</summary>
    /// <returns>The bounding rectangle, or <see cref="Rect.Empty"/> when nothing is loaded.</returns>
    public Rect DataBounds() => DataBounds(selectedOnly: false);

    /// <summary>Gets the union of feature bounds, optionally restricted to the selection.</summary>
    /// <param name="selectedOnly">When true, only selected features contribute.</param>
    /// <returns>The bounding rectangle, or <see cref="Rect.Empty"/> when nothing qualifies.</returns>
    public Rect DataBounds(bool selectedOnly)
    {
        if (_features is null)
        {
            return Rect.Empty;
        }

        Rect bounds = Rect.Empty;

        foreach (MapFeatureViewModel feature in _features)
        {
            if (selectedOnly && !feature.IsSelected)
            {
                continue;
            }

            if (string.IsNullOrEmpty(feature.PathData))
            {
                continue;
            }

            try
            {
                bounds.Union(Geometry.Parse(feature.PathData).Bounds);
            }
            catch (FormatException)
            {
                // One unparseable path must not prevent the rest from being framed.
            }
        }

        return bounds;
    }

    public MapFeatureViewModel? HitTest(double x, double y)
    {
        // Hit test against the base visual
        HitTestResult result = VisualTreeHelper.HitTest(_baseVisual, new Point(x, y));
        
        // To accurately map back to MapFeatureViewModel without storing metadata in the visual tree, 
        // we can either embed tags in drawing, or do a geometric intersection check.
        // For WPF DrawingVisual, the easiest way to map geometry back to viewmodel is to do a manual bounds/point check on the features.
        
        if (_features == null)
        {
            return null;
        }

        // Convert point to world coordinates
        var worldPoint = new Point((x - _offsetX) / _scale, (y - _offsetY) / _scale);

        // Simple hit testing logic: Find first feature where geometry contains point or stroke contains point
        foreach (var feature in _features.Reverse())
        {
            if (string.IsNullOrEmpty(feature.PathData))
            {
                continue;
            }
            try
            {
                var geometry = Geometry.Parse(feature.PathData);
                if (geometry.FillContains(worldPoint) || geometry.StrokeContains(new Pen(Brushes.Black, feature.StrokeThickness / _scale), worldPoint))
                {
                    return feature;
                }
            }
            catch { }
        }

        return null;
    }

    protected override HitTestResult HitTestCore(PointHitTestParameters hitTestParameters)
    {
        return new PointHitTestResult(this, hitTestParameters.HitPoint);
    }

    public IEnumerable<MapFeatureViewModel> SelectInBox(double minX, double minY, double maxX, double maxY)
    {
        var selected = new List<MapFeatureViewModel>();
        if (_features == null)
        {
            return selected;
        }

        var box = new Rect(new Point(minX, minY), new Point(maxX, maxY));
        
        foreach (var feature in _features)
        {
            if (string.IsNullOrEmpty(feature.PathData))
            {
                continue;
            }
            try
            {
                var geometry = Geometry.Parse(feature.PathData);
                if (box.Contains(geometry.Bounds))
                {
                    selected.Add(feature);
                }
            }
            catch { }
        }
        
        return selected;
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        _ = RenderAsync();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        
        var zoom = e.Delta > 0 ? 1.2 : 1 / 1.2;
        var position = e.GetPosition(this);

        _scale *= zoom;
        _offsetX = (_offsetX - position.X) * zoom + position.X;
        _offsetY = (_offsetY - position.Y) * zoom + position.Y;
        
        if (DataContext is MappingEditorViewModel vm)
        {
            vm.ScaleText = $"Scale 1:{(int)Math.Max(1, 100 / _scale)}";
        }

        _ = RenderAsync();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            _currentMeasurementPoints.Clear();
            DrawActiveMeasurement(new Point(0,0)); // Clear
            
            var vm = DataContext as MappingEditorViewModel;
            if (vm != null)
            {
                vm.CurrentToolMode = MapToolMode.Select;
            }
        }
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus(); // Required for OnKeyDown
        var mousePos = e.GetPosition(this);
        var vm = DataContext as MappingEditorViewModel;

        if (vm == null)
        {
            return;
        }

        if (vm.CurrentToolMode == MapToolMode.Pan)
        {
            _isPanning = true;
            _lastPanPosition = mousePos;
            CaptureMouse();
        }
        else if (vm.CurrentToolMode == MapToolMode.Select)
        {
            var hitFeature = HitTest(mousePos.X, mousePos.Y);
            if (hitFeature != null)
            {
                vm.SelectFeatureCommand.Execute(hitFeature);
            }
        }
        else if (vm.CurrentToolMode == MapToolMode.MeasureDistance || vm.CurrentToolMode == MapToolMode.MeasureArea)
        {
            var worldPt = ScreenToWorld(mousePos);
            if (_currentSnap != null)
            {
                worldPt = _currentSnap.Position;
            }

            _currentMeasurementPoints.Add(worldPt);
            DrawActiveMeasurement(mousePos);
        }
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        
        if (_isPanning)
        {
            _isPanning = false;
            ReleaseMouseCapture();
        }
        
        if (_isSelecting)
        {
            _isSelecting = false;
            ReleaseMouseCapture();
            
            // Execute box selection
            var end = e.GetPosition(this);
            // In a full implementation, we would query features inside this box
            // var selected = SelectInBox(_selectionStart.X, _selectionStart.Y, end.X, end.Y);
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var mousePos = e.GetPosition(this);
        var vm = DataContext as MappingEditorViewModel;
        if (vm == null)
        {
            return;
        }

        // Update Coordinate status bar
        var worldPt = ScreenToWorld(mousePos);
        vm.MouseWorldX = worldPt.X;
        vm.MouseWorldY = worldPt.Y;

        if (_isPanning)
        {
            var delta = mousePos - _lastPanPosition;
            _offsetX += delta.X;
            _offsetY += delta.Y;
            _lastPanPosition = mousePos;

            _ = RenderAsync();
        }
        else
        {
            // Handle Snapping
            _currentSnap = null;
            if (vm.SnappingSettings.IsSnappingEnabled)
            {
                var tolerance = vm.SnappingSettings.SnapTolerance / _scale; // World tolerance
                var activeModes = vm.SnappingSettings.GetActiveModes();
                _currentSnap = _snapService.SnapPoint(worldPt, tolerance, activeModes);
            }
            DrawSnapMarker();

            if (vm.CurrentToolMode == MapToolMode.Select)
            {
                var hitFeature = HitTest(mousePos.X, mousePos.Y);
                if (hitFeature != _hoveredFeature)
                {
                    _hoveredFeature = hitFeature;
                    DrawHoverLayer();
                }
            }
            else if (vm.CurrentToolMode == MapToolMode.MeasureDistance || vm.CurrentToolMode == MapToolMode.MeasureArea)
            {
                DrawActiveMeasurement(mousePos);
            }
        }
    }

    private Point ScreenToWorld(Point screenPt)
    {
        return new Point((screenPt.X - _offsetX) / _scale, (screenPt.Y - _offsetY) / _scale);
    }

    private Point WorldToScreen(Point worldPt)
    {
        return new Point(worldPt.X * _scale + _offsetX, worldPt.Y * _scale + _offsetY);
    }
}
