using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace AiGisConverter.MappingEditor.Presentation.Views;

public partial class MappingEditorView : UserControl
{
    public MappingEditorView()
    {
        InitializeComponent();

        // The view model decides when to frame the data; the control knows how.
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is ViewModels.MappingEditorViewModel previous)
        {
            previous.ZoomToDataRequested -= OnZoomToDataRequested;
            previous.ZoomToSelectionRequested -= OnZoomToSelectionRequested;
            previous.SelectionVisualsInvalidated -= OnSelectionVisualsInvalidated;
            previous.FlashSelectionRequested -= OnFlashSelectionRequested;
            previous.ScrollRowIntoViewRequested -= OnScrollRowIntoViewRequested;
        }

        if (e.NewValue is ViewModels.MappingEditorViewModel current)
        {
            current.ZoomToDataRequested += OnZoomToDataRequested;
            current.ZoomToSelectionRequested += OnZoomToSelectionRequested;
            current.SelectionVisualsInvalidated += OnSelectionVisualsInvalidated;
            current.FlashSelectionRequested += OnFlashSelectionRequested;
            current.ScrollRowIntoViewRequested += OnScrollRowIntoViewRequested;
        }
    }

    private void OnFlashSelectionRequested(object? sender, System.EventArgs e) =>
        MapRenderer.FlashSelection();

    private void OnScrollRowIntoViewRequested(object? sender, ViewModels.MapFeatureViewModel feature) =>
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Background,
            new System.Action(() =>
            {
                // Only scroll when the table is actually open; otherwise this is wasted layout work.
                if (AttributeGrid.IsVisible && AttributeGrid.Items.Contains(feature))
                {
                    AttributeGrid.ScrollIntoView(feature);
                }
            }));

    private void OnZoomToSelectionRequested(object? sender, System.EventArgs e) =>
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new System.Action(() => MapRenderer.ZoomToSelection()));

    private void OnSelectionVisualsInvalidated(object? sender, System.EventArgs e) =>
        MapRenderer.InvalidateDynamicLayers();

    private void OnZoomToDataRequested(object? sender, System.EventArgs e)
    {
        // Queued at Loaded priority so the control has been measured — zooming to fit needs a
        // real ActualWidth, and a conversion can finish before the panel has been laid out.
        Dispatcher.BeginInvoke(
            System.Windows.Threading.DispatcherPriority.Loaded,
            new System.Action(() => MapRenderer.ZoomToData()));
    }

    private void ToolBar_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is ToolBar toolBar)
        {
            if (toolBar.Template.FindName("OverflowGrid", toolBar) is FrameworkElement overflowGrid)
            {
                overflowGrid.Visibility = Visibility.Collapsed;
            }
            if (toolBar.Template.FindName("MainPanelBorder", toolBar) is FrameworkElement mainPanelBorder)
            {
                mainPanelBorder.Margin = new Thickness(0);
            }
        }
    }

    private void BtnFitExtents_Click(object sender, RoutedEventArgs e) => MapRenderer.ZoomToData();

    private void BtnResetView_Click(object sender, RoutedEventArgs e) => MapRenderer.UpdateViewport(1.0, 0, 0);
}
