using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;
using System.Text.Json;
using System.Windows.Input;
using AiGisConverter.Application.Abstractions;
using AiGisConverter.Business.Classification;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.MappingEditor.Application;
using AiGisConverter.MappingEditor.Business;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiGisConverter.MappingEditor.Presentation.ViewModels;

public partial class MappingEditorViewModel : ObservableObject
{
    // Built once: JsonSerializerOptions caches its converter lookups internally, so creating a new
    // instance per export throws that cache away and re-resolves every converter each time.
    private static readonly JsonSerializerOptions GeoJsonOptions = CreateGeoJsonOptions();

    private static JsonSerializerOptions CreateGeoJsonOptions()
    {
        JsonSerializerOptions options = new() { WriteIndented = true };
        options.Converters.Add(new NetTopologySuite.IO.Converters.GeoJsonConverterFactory());

        return options;
    }

    private readonly IMappingEditorService _editorService;
    private readonly LiveSimulationService _simulationService;
    private readonly StatisticsService _statisticsService;
    private readonly RuleValidator _validator;
    private readonly IConversionSession _session;

    // --- Project Explorer, bound to the current conversion session (Slice 1) ---

    [ObservableProperty]
    private bool _hasSession;

    [ObservableProperty]
    private string _currentDxfName = "(no drawing loaded)";

    [ObservableProperty]
    private string _sessionDrawingSize = "-";

    [ObservableProperty]
    private int _sessionFeatureCount;

    [ObservableProperty]
    private int _sessionLayerCount;

    [ObservableProperty]
    private string _sessionInputCrs = "Unknown";

    [ObservableProperty]
    private string _sessionOutputCrs = "Unknown";

    [ObservableProperty]
    private ObservableCollection<SessionLayer> _sessionLayers = new();

    [ObservableProperty]
    private ObservableCollection<SessionEntityType> _sessionEntityTypes = new();

    [ObservableProperty]
    private string _georeferenceWarning = string.Empty;

    [ObservableProperty]
    private string _statusExtentText = "-";

    [ObservableProperty]
    private ConversionSummary _conversionSummary = ConversionSummary.None;

    [ObservableProperty]
    private ObservableCollection<ConversionSessionSnapshot> _sessionHistory = new();

    [ObservableProperty]
    private SessionLayer? _selectedSessionLayer;

    [ObservableProperty]
    private SessionEntityType? _selectedSessionEntityType;

    [ObservableProperty]
    private string _layerFilterText = "All layers";

    [ObservableProperty]
    private int _selectedFeatureCount;

    [ObservableProperty]
    private MapFeatureViewModel? _selectedMapFeature;

    [ObservableProperty]
    private ICollectionView? _attributeRows;

    [ObservableProperty]
    private string _attributeSearchText = string.Empty;

    [ObservableProperty]
    private bool _showSelectedOnly;

    [ObservableProperty]
    private bool _isAttributeTableVisible;

    // Guards the two-way sync: selecting a layer in the explorer filters the map, and selecting a
    // feature on the map selects its layer. Without this the two would drive each other in a loop.
    private bool _synchronising;

    // The snapshot the map is currently built from, so a filter can be re-applied without
    // going back to the session.
    private ConversionSessionSnapshot? _mapSource;

    private readonly Stack<string> _undoStack = new();
    private readonly Stack<string> _redoStack = new();
    private bool _isRestoringState;

    [ObservableProperty]
    private MappingProfile _currentProfile = new();

    [ObservableProperty]
    private MappingRuleViewModel? _selectedRule;

    [ObservableProperty]
    private ObservableCollection<MappingRuleViewModel> _rules = new();

    [ObservableProperty]
    private ICollectionView? _groupedRules;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<ValidationIssue> _validationIssues = new();

    [ObservableProperty]
    private ObservableCollection<SimulationResult> _simulationResults = new();

    [ObservableProperty]
    private ObservableCollection<MapFeatureViewModel> _mapFeatures = new();

    [ObservableProperty]
    private string _debuggerText = "Select a feature on the map or run simulation...";

    [ObservableProperty]
    private string _coordinateText = "X: 0.00, Y: 0.00";

    [ObservableProperty]
    private double _mouseWorldX;

    [ObservableProperty]
    private double _mouseWorldY;

    [ObservableProperty]
    private string _scaleText = "Scale 1:1";

    [ObservableProperty]
    private bool _isStatisticsVisible = false;

    [ObservableProperty]
    private ObservableCollection<string> _availableLayers = new();

    [ObservableProperty]
    private ObservableCollection<string> _availableEntityTypes = new();

    [ObservableProperty]
    private bool _hasPendingRules = false;

    private readonly List<MappingRule> _pendingRules = new();

    [ObservableProperty]
    private bool _hasActiveAiSuggestion = false;

    private MappingRule? _activeAiSuggestion;

    [ObservableProperty]
    private MapToolMode _currentToolMode = MapToolMode.Select;

    [ObservableProperty]
    private SnappingSettingsViewModel _snappingSettings = new();

    [ObservableProperty]
    private ObservableCollection<MeasurementViewModel> _measurements = new();

    private readonly Stack<string> _undoMeasurementStack = new();
    private readonly Stack<string> _redoMeasurementStack = new();

    public MappingEditorViewModel(IMappingEditorService editorService, LiveSimulationService simulationService, StatisticsService statisticsService, RuleValidator validator, IConversionSession session)
    {
        _editorService = editorService;
        _simulationService = simulationService;
        _statisticsService = statisticsService;
        _validator = validator;
        _session = session;

        LoadProfiles();

        // Show whatever has already been converted, then track future conversions.
        _session.Changed += OnSessionChanged;
        RefreshFromSession();
    }

    /// <summary>Marshals a session change onto the UI thread and refreshes the explorer.</summary>
    private void OnSessionChanged(object? sender, EventArgs e)
    {
        System.Windows.Application dispatcherOwner = System.Windows.Application.Current;

        if (dispatcherOwner?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(RefreshFromSession);
        }
        else
        {
            RefreshFromSession();
        }
    }

    /// <summary>Rebuilds the Project Explorer from the current session snapshot.</summary>
    private void RefreshFromSession()
    {
        ConversionSessionSnapshot? snapshot = _session.Current;

        SessionHistory = new ObservableCollection<ConversionSessionSnapshot>(_session.History);

        if (snapshot is null)
        {
            HasSession = false;
            CurrentDxfName = "(no drawing loaded)";
            SessionDrawingSize = "-";
            SessionFeatureCount = 0;
            SessionLayerCount = 0;
            SessionInputCrs = "Unknown";
            SessionOutputCrs = "Unknown";
            SessionLayers = new ObservableCollection<SessionLayer>();
            SessionEntityTypes = new ObservableCollection<SessionEntityType>();
            GeoreferenceWarning = string.Empty;
            StatusExtentText = "-";
            ConversionSummary = ConversionSummary.None;
            MapFeatures.Clear();
            return;
        }

        HasSession = true;
        CurrentDxfName = snapshot.DxfFileName;
        SessionDrawingSize = snapshot.SourceExtent.IsEmpty
            ? "-"
            : $"{snapshot.SourceExtent.Width:N1} × {snapshot.SourceExtent.Height:N1}";
        SessionFeatureCount = snapshot.FeatureCount;
        SessionLayerCount = snapshot.LayerCount;
        SessionInputCrs = snapshot.SourceCrs?.Identifier ?? "Local / unknown";
        SessionOutputCrs = snapshot.TargetCrs.Identifier;
        SessionLayers = new ObservableCollection<SessionLayer>(snapshot.Layers);
        SessionEntityTypes = new ObservableCollection<SessionEntityType>(snapshot.EntityTypes);
        ConversionSummary = snapshot.Summary;

        // Rule preview must run against this drawing, not invented geometry. Each element carries
        // the real layer and geometry, which is what the rules actually match on.
        _simulationService.SetElements([.. snapshot.Features.Select(ToSourceElement)]);

        AvailableLayers.Clear();

        foreach (SessionLayer layer in snapshot.Layers)
        {
            AvailableLayers.Add($"{layer.Name} ({layer.EntityCount})");
        }

        AvailableEntityTypes.Clear();

        foreach (SessionEntityType entityType in snapshot.EntityTypes)
        {
            AvailableEntityTypes.Add($"{entityType.Name} ({entityType.Count})");
        }

        GeoreferenceWarning = snapshot.IsGeoreferenced
            ? string.Empty
            : "Drawing is in local/projected coordinates and is not georeferenced — it is shown in "
              + "its own coordinates and cannot be overlaid on a world basemap.";

        StatusExtentText = snapshot.TransformedExtent.IsEmpty
            ? "-"
            : $"X {snapshot.TransformedExtent.MinX:N1}..{snapshot.TransformedExtent.MaxX:N1}  "
              + $"Y {snapshot.TransformedExtent.MinY:N1}..{snapshot.TransformedExtent.MaxY:N1}";

        DebuggerText =
            $"Loaded {snapshot.DxfFileName}\n"
            + $"{snapshot.FeatureCount} features · {snapshot.LayerCount} layers\n"
            + $"Input CRS: {SessionInputCrs}   Output CRS: {SessionOutputCrs}"
            + (GeoreferenceWarning.Length == 0 ? string.Empty : "\n\n" + GeoreferenceWarning);

        BuildMapFromSession(snapshot);
        BuildAttributeTable();
        RefreshStatisticsFromMap();

        // A newly converted drawing is framed automatically; requiring a manual Fit Extents after
        // every conversion is the kind of step people assume is a bug when they forget it.
        ZoomToDataRequested?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Projects a converted feature back onto a source element for rule preview.</summary>
    /// <remarks>
    /// The rule engine matches on the layer name and geometry family, so those are what this
    /// carries. It is the drawing's own data, simply reshaped for the simulator.
    /// </remarks>
    /// <param name="feature">The converted feature.</param>
    /// <returns>An element the rule simulator can evaluate.</returns>
    private static SourceElement ToSourceElement(Domain.Entities.Gis.GisFeature feature)
    {
        SourceElement element = new(feature.SourceElementId, feature.FeatureClass.Geometry)
        {
            Geometry = feature.Geometry,
        };

        element.SetAttribute("Layer", feature.SourceLayer.Value);

        return element;
    }

    /// <summary>
    /// Raised when the map should frame the loaded data.
    /// </summary>
    /// <remarks>
    /// Zooming is a view concern — it depends on the control's pixel size — so the view model asks
    /// rather than computes. This keeps the "fit the new drawing" decision in the view model and
    /// the arithmetic in the control.
    /// </remarks>
    public event EventHandler? ZoomToDataRequested;

    /// <summary>Asks the map to frame the loaded data.</summary>
    [RelayCommand]
    private void ZoomToData() => ZoomToDataRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Rebuilds the map whenever the operator isolates a layer, or clears the filter.</summary>
    partial void OnSelectedSessionLayerChanged(SessionLayer? value)
    {
        LayerFilterText = value is null
            ? "All layers"
            : $"Showing {value.Name} ({value.EntityCount})";

        // When this came from a map click, the map is already correct; rebuilding it would discard
        // the very selection that triggered the synchronisation.
        if (_synchronising)
        {
            return;
        }

        if (_mapSource is not null)
        {
            BuildMapFromSession(_mapSource);
        }
    }

    /// <summary>
    /// Highlights a rule's matches as soon as it is selected in the Rules list.
    /// </summary>
    /// <remarks>
    /// Driven from the existing <c>SelectedRule</c> binding rather than a separate command, so
    /// picking a rule in the list, the Properties panel and the map stay one action.
    /// </remarks>
    partial void OnSelectedRuleChanged(MappingRuleViewModel? value)
    {
        if (value is not null && MapFeatures.Count > 0 && !_synchronising)
        {
            SelectRuleCommand.Execute(value);
        }
    }

    /// <summary>Raised when the map should frame the current selection.</summary>
    public event EventHandler? ZoomToSelectionRequested;

    /// <summary>Raised when the highlight layer must be redrawn after a selection change.</summary>
    public event EventHandler? SelectionVisualsInvalidated;

    /// <summary>Gets the entity types the operator has isolated. Empty means "show everything".</summary>
    public ObservableCollection<string> SelectedEntityTypes { get; } = [];

    /// <summary>
    /// Selects every feature a rule would match, frames them, and reports the coverage.
    /// </summary>
    /// <remarks>
    /// Matching mirrors the rule engine's own criteria — layer names and entity types — so what the
    /// map highlights is what the rule will actually classify. Anything else would be a second,
    /// divergent interpretation of the same rule.
    /// </remarks>
    /// <param name="rule">The rule to preview.</param>
    [RelayCommand]
    private void SelectRule(MappingRuleViewModel? rule)
    {
        if (rule is null)
        {
            return;
        }

        SelectedRule = rule;

        HashSet<string> layers = new(rule.Model.LayerNames ?? [], StringComparer.OrdinalIgnoreCase);
        HashSet<string> entities = new(rule.Model.EntityTypes ?? [], StringComparer.OrdinalIgnoreCase);
        string target = rule.Model.TargetFeatureClass;

        int matched = ApplySelection(feature =>
            (layers.Count > 0 && layers.Contains(feature.SourceLayer))
            || (entities.Count > 0 && entities.Contains(feature.EntityType))
            || (layers.Count == 0 && entities.Count == 0
                && !string.IsNullOrEmpty(target)
                && string.Equals(feature.FeatureClassName, target, StringComparison.OrdinalIgnoreCase)));

        double coverage = MapFeatures.Count == 0 ? 0d : (double)matched / MapFeatures.Count;

        DebuggerText =
            $"Rule '{rule.RuleName}' → {rule.TargetFeatureClass}\n"
            + $"Matches {matched:N0} of {MapFeatures.Count:N0} features ({coverage:P1}).\n"
            + $"Layers: {(layers.Count == 0 ? "any" : string.Join(", ", layers))}\n"
            + $"Entity types: {(entities.Count == 0 ? "any" : string.Join(", ", entities))}";

        if (matched > 0)
        {
            ZoomToSelectionRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Adds or removes an entity type from the isolation set, and re-applies it.</summary>
    /// <param name="entityType">The native CAD type, for example <c>LWPOLYLINE</c>.</param>
    [RelayCommand]
    private void ToggleEntityType(string? entityType)
    {
        if (string.IsNullOrWhiteSpace(entityType))
        {
            return;
        }

        // The Project Explorer shows "LWPOLYLINE (305)"; match on the name before the count.
        string name = entityType.Split('(')[0].Trim();

        if (!SelectedEntityTypes.Remove(name))
        {
            SelectedEntityTypes.Add(name);
        }

        ApplyEntityTypeSelection();
    }

    /// <summary>Clears the entity-type isolation.</summary>
    [RelayCommand]
    private void ClearEntityTypes()
    {
        SelectedEntityTypes.Clear();
        ApplyEntityTypeSelection();
    }

    private void ApplyEntityTypeSelection()
    {
        if (SelectedEntityTypes.Count == 0)
        {
            ApplySelection(static _ => false);
            DebuggerText = "Entity-type filter cleared; showing every entity.";
            return;
        }

        HashSet<string> wanted = new(SelectedEntityTypes, StringComparer.OrdinalIgnoreCase);
        int matched = ApplySelection(feature => wanted.Contains(feature.EntityType));

        DebuggerText =
            $"Entity types: {string.Join(", ", SelectedEntityTypes)}\n"
            + $"{matched:N0} of {MapFeatures.Count:N0} features selected.";

        if (matched > 0)
        {
            ZoomToSelectionRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Points the Project Explorer, Rules panel and Properties panel at a clicked feature.
    /// </summary>
    /// <remarks>
    /// Reverse synchronisation: the map is the one place an operator can point at a specific
    /// thing, so a click there should answer "which layer, which entity type, which rule?" without
    /// them hunting through the tree. Guarded so the resulting selections do not re-drive the map.
    /// </remarks>
    /// <param name="feature">The feature that was clicked.</param>
    private void SynchroniseExplorerTo(MapFeatureViewModel feature)
    {
        if (_synchronising)
        {
            return;
        }

        _synchronising = true;

        try
        {
            SelectedSessionLayer = SessionLayers
                .FirstOrDefault(layer => string.Equals(layer.Name, feature.SourceLayer, StringComparison.OrdinalIgnoreCase));

            SelectedSessionEntityType = SessionEntityTypes
                .FirstOrDefault(type => string.Equals(type.Name, feature.EntityType, StringComparison.OrdinalIgnoreCase));

            if (MatchingRuleFor(feature) is { } rule)
            {
                SelectedRule = rule;
            }
        }
        finally
        {
            _synchronising = false;
        }
    }

    /// <summary>Finds the first rule in the current profile that would match a feature.</summary>
    /// <param name="feature">The feature to test.</param>
    /// <returns>The matching rule, or null when none applies.</returns>
    private MappingRuleViewModel? MatchingRuleFor(MapFeatureViewModel feature) =>
        Rules.FirstOrDefault(rule =>
            (rule.Model.LayerNames?.Contains(feature.SourceLayer, StringComparer.OrdinalIgnoreCase) ?? false)
            || (rule.Model.EntityTypes?.Contains(feature.EntityType, StringComparer.OrdinalIgnoreCase) ?? false)
            || string.Equals(rule.Model.TargetFeatureClass, feature.FeatureClassName, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Applies a selection predicate across the map and refreshes the highlight layer.
    /// </summary>
    /// <param name="predicate">Returns true for features that should be selected.</param>
    /// <returns>How many features were selected.</returns>
    private int ApplySelection(Func<MapFeatureViewModel, bool> predicate)
    {
        int matched = 0;

        foreach (MapFeatureViewModel feature in MapFeatures)
        {
            bool selected = predicate(feature);
            feature.IsSelected = selected;

            if (selected)
            {
                matched++;
            }
        }

        SelectedFeatureCount = matched;
        SelectionVisualsInvalidated?.Invoke(this, EventArgs.Empty);
        RefreshStatisticsFromMap();

        return matched;
    }

    /// <summary>
    /// Recomputes the dashboard from what is actually on the map.
    /// </summary>
    /// <remarks>
    /// The statistics service summarises a rule simulation, which describes invented sample data
    /// rather than the drawing on screen. Counting the map's own features keeps the dashboard
    /// honest, and narrows to the selection when one exists so "what did I just select?" is
    /// answered by the same numbers.
    /// </remarks>
    private void RefreshStatisticsFromMap()
    {
        if (MapFeatures.Count == 0)
        {
            Statistics = new ClassificationStatistics();
            return;
        }

        List<MapFeatureViewModel> scope = SelectedFeatureCount > 0
            ? [.. MapFeatures.Where(static feature => feature.IsSelected)]
            : [.. MapFeatures];

        ClassificationStatistics statistics = new()
        {
            TotalFeatures = scope.Count,
            UnclassifiedFeatures = scope.Count(feature =>
                string.IsNullOrWhiteSpace(feature.FeatureClassName)
                || string.Equals(feature.FeatureClassName, "Unclassified", StringComparison.OrdinalIgnoreCase)),
        };

        foreach (IGrouping<string, MapFeatureViewModel> group in scope
            .GroupBy(static feature => string.IsNullOrWhiteSpace(feature.FeatureClassName)
                ? "Unclassified"
                : feature.FeatureClassName, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count()))
        {
            statistics.FeatureClassCounts[group.Key] = group.Count();
        }

        foreach (IGrouping<string, MapFeatureViewModel> group in scope
            .GroupBy(static feature => string.IsNullOrWhiteSpace(feature.EntityType)
                ? "Unknown"
                : feature.EntityType, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(static group => group.Count()))
        {
            statistics.RuleUsageCounts[group.Key] = group.Count();
        }

        statistics.AverageConfidence = statistics.TotalFeatures == 0
            ? 0d
            : (double)(statistics.TotalFeatures - statistics.UnclassifiedFeatures) / statistics.TotalFeatures;

        Statistics = statistics;
    }

    /// <summary>Isolates a layer on the map, or clears the filter when it is already isolated.</summary>
    /// <param name="layer">The layer to show alone.</param>
    [RelayCommand]
    private void FilterByLayer(SessionLayer? layer) =>
        SelectedSessionLayer = layer is not null && layer == SelectedSessionLayer ? null : layer;

    /// <summary>Clears the layer filter and shows the whole drawing again.</summary>
    [RelayCommand]
    private void ClearLayerFilter() => SelectedSessionLayer = null;

    /// <summary>Makes an earlier conversion current again, refreshing every panel.</summary>
    /// <param name="snapshot">The conversion to reopen, from the history list.</param>
    [RelayCommand]
    private void ReopenSession(ConversionSessionSnapshot? snapshot)
    {
        if (snapshot is not null)
        {
            _session.Reopen(snapshot);
        }
    }

    /// <summary>
    /// Rebuilds the map from the session's converted features, in the output coordinate system.
    /// </summary>
    /// <remarks>
    /// The features are already in the output CRS — the pipeline transformed them before this point
    /// — so rendering them here is the transformed drawing, not raw DXF coordinates. The path data is
    /// fit to a nominal canvas the way the rule preview is, so the renderer's own pan and zoom apply
    /// on top.
    /// </remarks>
    /// <param name="snapshot">The current session snapshot.</param>
    private void BuildMapFromSession(ConversionSessionSnapshot snapshot)
    {
        _mapSource = snapshot;
        MapFeatures.Clear();

        string? layerFilter = SelectedSessionLayer?.Name;

        // The extent is measured over the whole drawing, not the filtered subset, so isolating a
        // layer does not make the map jump and rescale under the operator.
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;

        foreach (Domain.Entities.Gis.GisFeature feature in snapshot.Features)
        {
            if (feature.Geometry is { IsEmpty: false } geometry)
            {
                NetTopologySuite.Geometries.Envelope box = geometry.EnvelopeInternal;
                minX = Math.Min(minX, box.MinX);
                minY = Math.Min(minY, box.MinY);
                maxX = Math.Max(maxX, box.MaxX);
                maxY = Math.Max(maxY, box.MaxY);
            }
        }

        if (minX > maxX || minY > maxY)
        {
            return;
        }

        double width = maxX - minX;
        double height = maxY - minY;
        double scale = width > 0 && height > 0 ? Math.Min(800d / width, 600d / height) * 0.9d : 1d;

        foreach (Domain.Entities.Gis.GisFeature feature in snapshot.Features)
        {
            if (feature.Geometry is not { IsEmpty: false } geometry)
            {
                continue;
            }

            if (layerFilter is not null
                && !string.Equals(feature.SourceLayer.Value, layerFilter, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            (string stroke, string fill) = StyleFor(feature.FeatureClass.Geometry);

            string entityType = snapshot.EntityTypeByElementId.TryGetValue(feature.SourceElementId, out string? native)
                ? native
                : feature.FeatureClass.Geometry.ToString();

            MapFeatures.Add(new MapFeatureViewModel
            {
                PathData = Helpers.GeometryToSvgPathConverter.Convert(geometry, scale, minX, maxY),
                Stroke = stroke,
                Fill = fill,
                StrokeThickness = 1.4,
                TooltipText =
                    $"{feature.FeatureClass.Name}\nLayer: {feature.SourceLayer.Value}\nEntity: {entityType}",
                SourceLayer = feature.SourceLayer.Value,
                EntityType = entityType,
                FeatureClassName = feature.FeatureClass.Name,
                Handle = Attribute(feature, "Handle"),
                Color = Attribute(feature, "Color"),
                GeometryType = geometry.GeometryType,
                Length = Numeric(feature, "Length"),
                Area = Numeric(feature, "Area"),
                Classification = feature.Classification?.Label ?? feature.FeatureClass.Name,
                Confidence = feature.Classification?.Confidence.Value,
                RuleName = feature.Classification?.ProviderKey ?? string.Empty,
            });
        }
    }

    /// <summary>
    /// Builds the attribute table's view over the map features.
    /// </summary>
    /// <remarks>
    /// A <see cref="ICollectionView"/> over the same <see cref="MapFeatures"/> collection, not a
    /// copy: sorting or filtering the table cannot drift from what the map is drawing, and a
    /// selection made in one is the same object in the other.
    /// </remarks>
    private void BuildAttributeTable()
    {
        ICollectionView view = CollectionViewSource.GetDefaultView(MapFeatures);
        view.Filter = FilterAttributeRow;
        AttributeRows = view;
    }

    /// <summary>Applies the search box and the selected-only toggle to one row.</summary>
    /// <param name="item">The candidate row.</param>
    /// <returns><see langword="true"/> when the row should be shown.</returns>
    private bool FilterAttributeRow(object item)
    {
        if (item is not MapFeatureViewModel feature)
        {
            return false;
        }

        if (ShowSelectedOnly && !feature.IsSelected)
        {
            return false;
        }

        if (_hiddenFeatureIds.Count > 0 && _hiddenFeatureIds.Contains(feature.Handle))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(AttributeSearchText)
            || feature.SearchText.Contains(AttributeSearchText, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnAttributeSearchTextChanged(string value) => AttributeRows?.Refresh();

    partial void OnShowSelectedOnlyChanged(bool value) => AttributeRows?.Refresh();

    /// <summary>Raised when the map should pulse the current selection.</summary>
    public event EventHandler? FlashSelectionRequested;

    /// <summary>Raised when the attribute table should scroll the selected row into view.</summary>
    public event EventHandler<MapFeatureViewModel>? ScrollRowIntoViewRequested;

    /// <summary>Gets the saved selection sets, by name.</summary>
    public ObservableCollection<string> SelectionSetNames { get; } = [];

    private readonly Dictionary<string, HashSet<string>> _selectionSets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Pulses the current selection on the map.</summary>
    [RelayCommand]
    private void FlashSelection() => FlashSelectionRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>
    /// Extends the selection to every feature sharing the current one's layer and entity type.
    /// </summary>
    /// <remarks>
    /// "Similar" means the two things a CAD operator actually groups by. Matching on the GIS class
    /// instead would select things that merely ended up classified alike, which is a different
    /// question.
    /// </remarks>
    [RelayCommand]
    private void SelectSimilar()
    {
        MapFeatureViewModel? seed = SelectedMapFeature
            ?? MapFeatures.FirstOrDefault(static feature => feature.IsSelected);

        if (seed is null)
        {
            DebuggerText = "Select a feature first, then Select Similar extends it to matching ones.";
            return;
        }

        int matched = ApplySelection(feature =>
            string.Equals(feature.SourceLayer, seed.SourceLayer, StringComparison.OrdinalIgnoreCase)
            && string.Equals(feature.EntityType, seed.EntityType, StringComparison.OrdinalIgnoreCase));

        DebuggerText =
            $"Selected {matched:N0} features similar to the seed "
            + $"(layer {seed.SourceLayer}, entity {seed.EntityType}).";
    }

    /// <summary>Inverts the selection across every feature on the map.</summary>
    [RelayCommand]
    private void InvertSelection()
    {
        HashSet<MapFeatureViewModel> previouslySelected =
            [.. MapFeatures.Where(static feature => feature.IsSelected)];

        int matched = ApplySelection(feature => !previouslySelected.Contains(feature));

        DebuggerText = $"Inverted the selection: {matched:N0} of {MapFeatures.Count:N0} features are now selected.";
    }

    /// <summary>Hides the selected features by filtering them out of the table and map.</summary>
    [RelayCommand]
    private void HideSelection()
    {
        foreach (MapFeatureViewModel feature in MapFeatures.Where(static f => f.IsSelected))
        {
            _hiddenFeatureIds.Add(feature.Handle);
        }

        ApplySelection(static _ => false);
        AttributeRows?.Refresh();
        DebuggerText = $"{_hiddenFeatureIds.Count:N0} features hidden. Use Show All to restore them.";
    }

    /// <summary>Restores every hidden feature.</summary>
    [RelayCommand]
    private void ShowAllFeatures()
    {
        _hiddenFeatureIds.Clear();
        AttributeRows?.Refresh();
        DebuggerText = "All hidden features restored.";
    }

    private readonly HashSet<string> _hiddenFeatureIds = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Copies the selected rows to the clipboard as tab-separated text.</summary>
    /// <remarks>Tab separated so it pastes straight into a spreadsheet as columns.</remarks>
    [RelayCommand]
    private void CopySelectedAttributes()
    {
        List<MapFeatureViewModel> rows = [.. MapFeatures.Where(static feature => feature.IsSelected)];

        if (rows.Count == 0)
        {
            DebuggerText = "Nothing is selected, so there was nothing to copy.";
            return;
        }

        StringBuilder text = new();
        text.AppendLine("Handle\tLayer\tEntity\tGeometry\tClass\tClassification\tConfidence\tStatus\tLength\tArea\tColour");

        foreach (MapFeatureViewModel row in rows)
        {
            text.AppendLine(string.Join('\t',
                row.Handle, row.SourceLayer, row.EntityType, row.GeometryType, row.FeatureClassName,
                row.Classification,
                row.Confidence?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                row.Status,
                row.Length?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                row.Area?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                row.Color));
        }

        try
        {
            System.Windows.Clipboard.SetText(text.ToString());
            DebuggerText = $"Copied {rows.Count:N0} rows to the clipboard.";
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            // The clipboard is a shared OS resource and another process can hold it briefly.
            DebuggerText = $"The clipboard was unavailable: {ex.Message}";
        }
    }

    /// <summary>Saves the current selection under a name so it can be recalled later.</summary>
    [RelayCommand]
    private void SaveSelectionSet()
    {
        HashSet<string> handles =
            [.. MapFeatures.Where(static f => f.IsSelected).Select(static f => f.Handle)];

        if (handles.Count == 0)
        {
            DebuggerText = "Select some features before saving a selection set.";
            return;
        }

        string name = $"Set {_selectionSets.Count + 1} ({handles.Count:N0})";
        _selectionSets[name] = handles;
        SelectionSetNames.Add(name);

        DebuggerText = $"Saved selection set '{name}'.";
    }

    /// <summary>Restores a previously saved selection set.</summary>
    /// <param name="name">The set to load.</param>
    [RelayCommand]
    private void LoadSelectionSet(string? name)
    {
        if (name is null || !_selectionSets.TryGetValue(name, out HashSet<string>? handles))
        {
            return;
        }

        int matched = ApplySelection(feature => handles.Contains(feature.Handle));
        DebuggerText = $"Restored '{name}': {matched:N0} features selected.";

        if (matched > 0)
        {
            ZoomToSelectionRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Writes the selected features to a GeoJSON file.</summary>
    /// <remarks>
    /// Written directly rather than through the GIS exporters: those need the conversion pipeline's
    /// dataset, while this is a quick "give me what I just picked" from the editor.
    /// </remarks>
    [RelayCommand]
    private void ExportSelectionAsGeoJson()
    {
        List<MapFeatureViewModel> rows = [.. MapFeatures.Where(static feature => feature.IsSelected)];

        if (rows.Count == 0 || _mapSource is null)
        {
            DebuggerText = "Nothing is selected, so there was nothing to export.";
            return;
        }

        HashSet<string> handles = [.. rows.Select(static r => r.Handle)];

        try
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"aigis-selection-{DateTime.Now:yyyyMMdd-HHmmss}.geojson");

            NetTopologySuite.Features.FeatureCollection collection = [];

            foreach (Domain.Entities.Gis.GisFeature feature in _mapSource.Features)
            {
                string handle = Attribute(feature, "Handle");

                if (!handles.Contains(handle) || feature.Geometry is not { IsEmpty: false } geometry)
                {
                    continue;
                }

                NetTopologySuite.Features.AttributesTable attributes = new()
                {
                    { "Handle", handle },
                    { "Layer", feature.SourceLayer.Value },
                    { "Class", feature.FeatureClass.Name },
                };

                collection.Add(new NetTopologySuite.Features.Feature(geometry, attributes));
            }

            File.WriteAllText(path, JsonSerializer.Serialize(collection, GeoJsonOptions));
            DebuggerText = $"Exported {rows.Count:N0} selected features to:\n{path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebuggerText = $"The selection could not be exported: {ex.Message}";
        }
    }

    /// <summary>Shows or hides the attribute table.</summary>
    [RelayCommand]
    private void ToggleAttributeTable() => IsAttributeTableVisible = !IsAttributeTableVisible;

    /// <summary>Selects a row's feature and synchronises every other panel to it.</summary>
    /// <param name="feature">The row that was activated.</param>
    [RelayCommand]
    private void SelectAttributeRow(MapFeatureViewModel? feature)
    {
        if (feature is not null)
        {
            SelectFeatureCommand.Execute(feature);
        }
    }

    /// <summary>Frames the selected rows on the map. Bound to double-click and the context menu.</summary>
    [RelayCommand]
    private void ZoomToSelectedRows()
    {
        if (SelectedFeatureCount > 0)
        {
            ZoomToSelectionRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>Hides everything except the current selection, on the map and in the table.</summary>
    [RelayCommand]
    private void IsolateSelection()
    {
        ShowSelectedOnly = true;
        ZoomToSelectedRows();
    }

    /// <summary>Restores the full table and map after an isolation.</summary>
    [RelayCommand]
    private void ClearIsolation()
    {
        ShowSelectedOnly = false;
        AttributeSearchText = string.Empty;
    }

    /// <summary>Writes the selected rows to a CSV file beside the drawing.</summary>
    /// <remarks>
    /// Deliberately CSV and deliberately local: this is the "send me that list" action, not a GIS
    /// export, and it must work even when the OGR-backed writers are unavailable.
    /// </remarks>
    [RelayCommand]
    private void ExportSelection()
    {
        List<MapFeatureViewModel> rows = [.. MapFeatures.Where(static feature => feature.IsSelected)];

        if (rows.Count == 0)
        {
            DebuggerText = "Nothing is selected, so there was nothing to export.";
            return;
        }

        try
        {
            string path = Path.Combine(
                Path.GetTempPath(),
                $"aigis-selection-{DateTime.Now:yyyyMMdd-HHmmss}.csv");

            StringBuilder csv = new();
            csv.AppendLine("Handle,Layer,EntityType,Geometry,Class,Classification,Confidence,Status,Length,Area,Color");

            foreach (MapFeatureViewModel row in rows)
            {
                csv.AppendLine(string.Join(',',
                    Csv(row.Handle), Csv(row.SourceLayer), Csv(row.EntityType), Csv(row.GeometryType),
                    Csv(row.FeatureClassName), Csv(row.Classification),
                    row.Confidence?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                    Csv(row.Status),
                    row.Length?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                    row.Area?.ToString("F3", CultureInfo.InvariantCulture) ?? string.Empty,
                    Csv(row.Color)));
            }

            File.WriteAllText(path, csv.ToString());
            DebuggerText = $"Exported {rows.Count:N0} selected features to:\n{path}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DebuggerText = $"The selection could not be exported: {ex.Message}";
        }
    }

    /// <summary>Quotes a CSV field so commas and quotes in CAD names cannot shift columns.</summary>
    private static string Csv(string? value) =>
        string.IsNullOrEmpty(value) ? string.Empty : "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    /// <summary>Drafts a rule from the selected rows' layer and entity type.</summary>
    [RelayCommand]
    private void CreateRuleFromSelection()
    {
        List<MapFeatureViewModel> rows = [.. MapFeatures.Where(static feature => feature.IsSelected)];

        if (rows.Count == 0)
        {
            DebuggerText = "Select some features first; the rule is drafted from what they have in common.";
            return;
        }

        string[] layers = [.. rows.Select(static r => r.SourceLayer).Distinct(StringComparer.OrdinalIgnoreCase)];
        string[] entities = [.. rows.Select(static r => r.EntityType).Distinct(StringComparer.OrdinalIgnoreCase)];

        SaveStateForUndo();

        MappingRule drafted = new()
        {
            RuleName = $"From selection ({layers.FirstOrDefault() ?? "mixed"})",
            TargetFeatureClass = rows[0].FeatureClassName,
            LayerNames = layers,
            EntityTypes = entities,
            Priority = 50,
        };

        CurrentProfile.Rules.Add(drafted);
        ReloadRulesCollection();

        DebuggerText =
            $"Drafted a rule from {rows.Count:N0} selected features.\n"
            + $"Layers: {string.Join(", ", layers)}\nEntity types: {string.Join(", ", entities)}";
    }

    /// <summary>Reads an attribute as display text, or empty when the feature does not carry it.</summary>
    /// <param name="feature">The converted feature.</param>
    /// <param name="name">The attribute name, matched case-insensitively.</param>
    /// <returns>The value as text, or an empty string.</returns>
    private static string Attribute(Domain.Entities.Gis.GisFeature feature, string name) =>
        feature.Attributes.TryGetValue(name, out Domain.ValueObjects.AttributeValue value) && !value.IsNull
            ? value.ToInvariantString()
            : string.Empty;

    /// <summary>Reads an attribute as a number, or null when absent or not numeric.</summary>
    /// <param name="feature">The converted feature.</param>
    /// <param name="name">The attribute name.</param>
    /// <returns>The numeric value, or null.</returns>
    private static double? Numeric(Domain.Entities.Gis.GisFeature feature, string name) =>
        feature.Attributes.TryGetValue(name, out Domain.ValueObjects.AttributeValue value)
        && !value.IsNull
        && double.TryParse(
            value.ToInvariantString(),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out double parsed)
            ? parsed
            : null;

    /// <summary>Chooses a stroke and fill for a geometry family.</summary>
    private static (string Stroke, string Fill) StyleFor(Domain.Enums.GeometryKind kind) => kind switch
    {
        Domain.Enums.GeometryKind.Polygon => ("#4FC3F7", "#224FC3F7"),
        Domain.Enums.GeometryKind.Line => ("#B0BEC5", "Transparent"),
        Domain.Enums.GeometryKind.Point or Domain.Enums.GeometryKind.Annotation => ("#81C784", "#2281C784"),
        _ => ("#FF8A65", "Transparent"),
    };

    private void LoadProfiles()
    {
        var profiles = _editorService.GetProfiles();
        if (profiles.Any())
        {
            CurrentProfile = profiles.First();
        }
        else
        {
            CurrentProfile = new MappingProfile { Name = "New Profile" };
        }

        _undoStack.Clear();
        _redoStack.Clear();
        SaveStateForUndo();

        ReloadRulesCollection();
    }

    private void ReloadRulesCollection()
    {
        foreach (var r in Rules)
        {
            r.PropertyChanged -= OnRulePropertyChanged;
        }
        
        Rules.Clear();
        foreach (var rule in CurrentProfile.Rules)
        {
            var vm = new MappingRuleViewModel(rule);
            vm.PropertyChanged += OnRulePropertyChanged;
            Rules.Add(vm);
        }

        GroupedRules = CollectionViewSource.GetDefaultView(Rules);
        GroupedRules.GroupDescriptions.Add(new PropertyGroupDescription(nameof(MappingRuleViewModel.TargetFeatureClass)));
        GroupedRules.Filter = FilterRules;
    }

    private bool FilterRules(object obj)
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }
        if (obj is MappingRuleViewModel ruleVm)
        {
            return ruleVm.RuleName.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ||
                   ruleVm.TargetFeatureClass.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }

    partial void OnSearchTextChanged(string value)
    {
        GroupedRules?.Refresh();
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_isRestoringState)
        {
            return;
        }

        SaveStateForUndo();

        // Live Rule Editing: Trigger a quick re-run when properties change.
        // We could debounce this in a production environment.
        _ = RunSimulationAsync();
    }

    [RelayCommand]
    private void NewProfile()
    {
        CurrentProfile = new MappingProfile { Name = "New Profile", ProfileId = Guid.NewGuid().ToString() };
        ReloadRulesCollection();
    }

    [RelayCommand]
    private void OpenProfile()
    {
        // In a full implementation, open a FileDialog to load a specific JSON.
        LoadProfiles();
    }

    private void SaveStateForUndo()
    {
        if (_isRestoringState)
        {
            return;
        }
        
        var state = JsonSerializer.Serialize(CurrentProfile.Rules);
        if (_undoStack.Count == 0 || _undoStack.Peek() != state)
        {
            _undoStack.Push(state);
            _redoStack.Clear();
        }
    }

    [RelayCommand]
    private void Undo()
    {
        if (_undoStack.Count <= 1)
        {
            return; // Keep at least the initial state
        }

        _isRestoringState = true;
        
        // Push current state to redo stack
        var currentState = _undoStack.Pop();
        _redoStack.Push(currentState);

        // Restore previous state
        var previousState = _undoStack.Peek();
        CurrentProfile.Rules = JsonSerializer.Deserialize<List<MappingRule>>(previousState) ?? new List<MappingRule>();
        
        ReloadRulesCollection();
        RunValidation();
        _ = RunSimulationAsync();

        _isRestoringState = false;
    }

    [RelayCommand]
    private void Redo()
    {
        if (_redoStack.Count == 0)
        {
            return;
        }

        _isRestoringState = true;

        var nextState = _redoStack.Pop();
        _undoStack.Push(nextState);

        CurrentProfile.Rules = JsonSerializer.Deserialize<List<MappingRule>>(nextState) ?? new List<MappingRule>();

        ReloadRulesCollection();
        RunValidation();
        _ = RunSimulationAsync();

        _isRestoringState = false;
    }

    [ObservableProperty]
    private ClassificationStatistics? _statistics;

    [RelayCommand]
    private void ShowStatistics()
    {
        IsStatisticsVisible = !IsStatisticsVisible;
    }

    [RelayCommand]
    private void AddRule()
    {
        SaveStateForUndo();
        var rule = new MappingRule { RuleName = "New Rule", TargetFeatureClass = "Feature" };
        CurrentProfile.Rules.Add(rule);
        var vm = new MappingRuleViewModel(rule);
        vm.PropertyChanged += OnRulePropertyChanged;
        Rules.Add(vm);
        SelectedRule = vm;
        RunValidation();
    }

    [RelayCommand]
    private void DeleteRule(MappingRuleViewModel ruleVm)
    {
        if (ruleVm != null)
        {
            SaveStateForUndo();
            ruleVm.PropertyChanged -= OnRulePropertyChanged;
            CurrentProfile.Rules.Remove(ruleVm.Model);
            Rules.Remove(ruleVm);
            RunValidation();
            _ = RunSimulationAsync();
        }
    }

    [RelayCommand]
    private void SaveProfile()
    {
        CurrentProfile.Rules = Rules.Select(r => r.Model).ToList();
        _editorService.SaveProfile(CurrentProfile, CurrentProfile.Name);
    }

    [RelayCommand]
    public void Validate()
    {
        RunValidation();
    }

    [RelayCommand]
    private void SetToolMode(MapToolMode mode)
    {
        CurrentToolMode = mode;
        if (mode == MapToolMode.Pan)
        {
            DebuggerText = "Pan tool active. Drag map to move.";
        }
        else if (mode == MapToolMode.MeasureDistance)
        {
            DebuggerText = "Measure Distance active. Click on the map to add points.";
        }
        else if (mode == MapToolMode.MeasureArea)
        {
            DebuggerText = "Measure Area active. Click to draw a polygon.";
        }
        else if (mode == MapToolMode.CoordinatePicker)
        {
            DebuggerText = "Coordinate Picker active. Click to pick coordinate.";
        }
        else
        {
            DebuggerText = "Select tool active. Click a feature to inspect.";
        }
    }

    [RelayCommand]
    private void DeleteMeasurement(MeasurementViewModel measurement)
    {
        if (measurement != null)
        {
            SaveMeasurementStateForUndo();
            Measurements.Remove(measurement);
        }
    }

    [RelayCommand]
    private void ClearMeasurements()
    {
        SaveMeasurementStateForUndo();
        Measurements.Clear();
    }

    public void SaveMeasurementStateForUndo()
    {
        var state = JsonSerializer.Serialize(Measurements);
        if (_undoMeasurementStack.Count == 0 || _undoMeasurementStack.Peek() != state)
        {
            _undoMeasurementStack.Push(state);
            _redoMeasurementStack.Clear();
        }
    }

    [RelayCommand]
    private void UndoMeasurement()
    {
        if (_undoMeasurementStack.Count == 0)
        {
            return;
        }
        
        var currentState = JsonSerializer.Serialize(Measurements);
        _redoMeasurementStack.Push(currentState);
        
        var previousState = _undoMeasurementStack.Pop();
        var restored = JsonSerializer.Deserialize<List<MeasurementViewModel>>(previousState) ?? new List<MeasurementViewModel>();
        Measurements.Clear();
        foreach (var m in restored)
        {
            Measurements.Add(m);
        }
    }

    [RelayCommand]
    private void RedoMeasurement()
    {
        if (_redoMeasurementStack.Count == 0)
        {
            return;
        }

        var currentState = JsonSerializer.Serialize(Measurements);
        _undoMeasurementStack.Push(currentState);
        
        var nextState = _redoMeasurementStack.Pop();
        var restored = JsonSerializer.Deserialize<List<MeasurementViewModel>>(nextState) ?? new List<MeasurementViewModel>();
        Measurements.Clear();
        foreach (var m in restored)
        {
            Measurements.Add(m);
        }
    }


    /// <summary>
    /// Selects a feature on the map and synchronises every other panel to it.
    /// </summary>
    /// <remarks>
    /// Works for features built from the conversion session, which carry no simulator result. The
    /// earlier version returned immediately when <c>Result</c> was null, so clicking a real
    /// converted feature did nothing at all — the common case in normal use.
    /// </remarks>
    /// <param name="featureVm">The feature that was clicked.</param>
    [RelayCommand]
    private void SelectFeature(MapFeatureViewModel featureVm)
    {
        if (featureVm is null)
        {
            return;
        }

        foreach (var f in MapFeatures)
        {
            f.IsSelected = false;
        }

        featureVm.IsSelected = true;
        SelectedFeatureCount = 1;
        SelectedMapFeature = featureVm;
        SelectionVisualsInvalidated?.Invoke(this, EventArgs.Empty);

        // Bring the matching row into view so the table follows the map rather than the operator
        // having to scroll a few thousand rows looking for what they just clicked.
        ScrollRowIntoViewRequested?.Invoke(this, featureVm);

        SynchroniseExplorerTo(featureVm);

        var res = featureVm.Result;

        // A session feature carries its own identity; only a simulated one has a rule result.
        if (res is null)
        {
            DebuggerText =
                $"--- Feature Selected ---\n"
                + $"Class: {featureVm.FeatureClassName}\n"
                + $"Layer: {featureVm.SourceLayer}\n"
                + $"Entity: {featureVm.EntityType}\n"
                + (MatchingRuleFor(featureVm) is { } matched
                    ? $"\n[MATCHED]\nRule: {matched.RuleName} → {matched.TargetFeatureClass}\n"
                    : "\n[UNCLASSIFIED]\nNo rule in the current profile matches this feature.\n");

            return;
        }

        var r = res.MatchedRule;

        var layerName = res.Element.Attributes.TryGetValue("Layer", out var l) ? l?.ToString() : "Unknown";

        DebuggerText = $"--- Feature Selected ---\n" +
                       $"Geometry: {res.Element.Geometry?.GeometryType ?? "Unknown"}\n" +
                       $"Layer: {layerName}\n";

        if (r != null)
        {
            DebuggerText += $"\n[MATCHED]\nRule: {r.RuleName}\nClass: {r.Label}\nConfidence: {r.Confidence.Value:P1}\n";
        }
        else
        {
            DebuggerText += $"\n[UNCLASSIFIED]\nNo rules matched with sufficient confidence.\n";
        }

        if (res.Candidates.Count > 0)
        {
            DebuggerText += $"\n[CANDIDATES]\n";
            foreach (var c in res.Candidates)
            {
                DebuggerText += $"- {c.RuleName} ({c.Label}): {c.Confidence.Value:P1}\n";
            }
        }
    }

    public void RunValidation()
    {
        CurrentProfile.Rules = Rules.Select(r => r.Model).ToList();
        var issues = _validator.ValidateProfile(CurrentProfile);
        ValidationIssues.Clear();
        foreach (var issue in issues)
        {
            ValidationIssues.Add(issue);
        }
    }

    [RelayCommand]
    public void RunSimulation()
    {
        _ = RunSimulationAsync();
    }

    [RelayCommand]
    private void GenerateInitialRules()
    {
        if (AvailableLayers.Count == 0)
        {
            return;
        }
        
        _pendingRules.Clear();
        int added = 0;
        foreach(var layerStr in AvailableLayers)
        {
            var parts = layerStr.Split(' ');
            var rawLayer = parts[0];
            
            if (rawLayer.Contains("TREE", StringComparison.OrdinalIgnoreCase))
            {
                _pendingRules.Add(new MappingRule { RuleName = "Tree (Point)", TargetFeatureClass = "Tree", LayerNames = new string[]{rawLayer}, Priority = 10 });
                added++;
            }
            else if (rawLayer.Contains("ROAD", StringComparison.OrdinalIgnoreCase))
            {
                _pendingRules.Add(new MappingRule { RuleName = "Road Centerline", TargetFeatureClass = "Road", LayerNames = new string[]{rawLayer}, Priority = 20 });
                added++;
            }
            else if (rawLayer.Contains("BLDG", StringComparison.OrdinalIgnoreCase))
            {
                _pendingRules.Add(new MappingRule { RuleName = "Building", TargetFeatureClass = "Building", LayerNames = new string[]{rawLayer}, Priority = 30 });
                added++;
            }
        }
        
        if (added > 0)
        {
            HasPendingRules = true;
            DebuggerText = $"[PREVIEW: AUTO GENERATION]\nFound {added} suggested mapping rules based on CAD heuristics.\n\n";
            foreach (var r in _pendingRules)
            {
                DebuggerText += $"- {r.RuleName} (Matches Layer: {r.LayerNames?.FirstOrDefault()})\n";
            }
            DebuggerText += "\nClick 'Accept Drafts' to apply these rules.";
        }
    }

    [RelayCommand]
    private void AcceptPendingRules()
    {
        if (!HasPendingRules)
        {
            return;
        }
        
        SaveStateForUndo();
        foreach (var rule in _pendingRules)
        {
            CurrentProfile.Rules.Add(rule);
        }
        _pendingRules.Clear();
        HasPendingRules = false;
        
        ReloadRulesCollection();
        _ = RunSimulationAsync();
        DebuggerText = "Draft rules applied successfully.";
    }

    [RelayCommand]
    private void AcceptAiSuggestion()
    {
        if (!HasActiveAiSuggestion || _activeAiSuggestion == null)
        {
            return;
        }
        
        SaveStateForUndo();
        CurrentProfile.Rules.Add(_activeAiSuggestion);
        _activeAiSuggestion = null;
        HasActiveAiSuggestion = false;
        
        ReloadRulesCollection();
        _ = RunSimulationAsync();
        DebuggerText = "AI Suggestion applied successfully.";
    }

    private async Task RunSimulationAsync()
    {
        CurrentProfile.Rules = Rules.Select(r => r.Model).ToList();
        
        // Push work to background to keep UI responsive during live editing
        var results = await Task.Run(() => _simulationService.Simulate(CurrentProfile));
        var elements = _simulationService.GetElements();
        
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            // The layer and entity-type pickers are filled from the conversion session when a
            // drawing is loaded, so nothing is derived from the simulator here any more.
            SimulationResults.Clear();

            // With a real drawing loaded, the map is driven by the conversion session, not the
            // rule simulator; leave the session's features on screen and only refresh the rule and
            // statistics panels below.
            if (!HasSession)
            {
                MapFeatures.Clear();
            }

            double minX = double.MaxValue, minY = double.MaxValue;
            double maxX = double.MinValue, maxY = double.MinValue;

            foreach (var element in elements)
            {
                if (element.Geometry != null)
                {
                    var env = element.Geometry.EnvelopeInternal;
                    if (!env.IsNull)
                    {
                        minX = Math.Min(minX, env.MinX);
                        minY = Math.Min(minY, env.MinY);
                        maxX = Math.Max(maxX, env.MaxX);
                        maxY = Math.Max(maxY, env.MaxY);
                    }
                }
            }
            
            double width = maxX - minX;
            double height = maxY - minY;
            double scale = 1.0;
            
            if (width > 0 && height > 0)
            {
                scale = Math.Min(800.0 / width, 600.0 / height) * 0.9;
            }

            int unclassifiedCount = 0;

            foreach (var r in results)
            {
                SimulationResults.Add(r);
                if (r.MatchedRule == null)
                {
                    unclassifiedCount++;
                }

                if (!HasSession && r.Element.Geometry != null)
                {
                    string stroke = "Red"; // Unknown
                    string fill = "Transparent";
                    
                    if (r.MatchedRule != null)
                    {
                        var target = r.MatchedRule.Label?.ToLowerInvariant() ?? "";
                        if (target.Contains("road"))
                        {
                            stroke = "Gray";
                        }
                        else if (target.Contains("building"))
                        {
                            stroke = "Brown";
                            fill = "#55A52A2A"; // Semi-transparent brown
                        }
                        else if (target.Contains("tree"))
                        {
                            stroke = "Green";
                            fill = "#55008000";
                        }
                        else if (target.Contains("utility"))
                        {
                            stroke = "Blue";
                        }
                        else
                        {
                            stroke = "Cyan";
                        }
                    }

                    var tooltip = r.MatchedRule != null 
                        ? $"Rule: {r.MatchedRule.RuleName}\nClass: {r.MatchedRule.Label}\nConfidence: {r.MatchedRule.Confidence.Value:P1}\nLayer: {r.Element.Attributes.GetValueOrDefault("Layer", "")}"
                        : $"Unclassified\nLayer: {r.Element.Attributes.GetValueOrDefault("Layer", "")}";

                    MapFeatures.Add(new MapFeatureViewModel
                    {
                        PathData = Helpers.GeometryToSvgPathConverter.Convert(r.Element.Geometry, scale, minX, maxY),
                        Stroke = stroke,
                        Fill = fill,
                        StrokeThickness = r.MatchedRule != null ? 2.0 : 1.0,
                        TooltipText = tooltip,
                        Result = r
                    });
                }
            }

            var stats = _statisticsService.CalculateStatistics(results);
            Statistics = stats;
            double coverage = stats.TotalFeatures > 0 ? (double)(stats.TotalFeatures - stats.UnclassifiedFeatures) / stats.TotalFeatures : 0;
            DebuggerText = $"Simulation Complete.\nTotal: {stats.TotalFeatures} | Unclassified: {stats.UnclassifiedFeatures} | Coverage: {coverage:P1}";

            // AI Rule Suggestion Heuristic
            if (unclassifiedCount > 5)
            {
                var mostCommonLayer = elements.Where(e => !results.Any(r => r.Element == e && r.MatchedRule != null))
                                              .Select(e => e.Attributes.TryGetValue("Layer", out var l) ? l?.ToString() : "Unknown")
                                              .GroupBy(l => l)
                                              .OrderByDescending(g => g.Count())
                                              .FirstOrDefault()?.Key ?? "Unknown";

                _activeAiSuggestion = new MappingRule 
                { 
                    RuleName = $"AI Generated ({mostCommonLayer})", 
                    TargetFeatureClass = "NewClass", 
                    LayerNames = new string[]{mostCommonLayer}
                };
                
                HasActiveAiSuggestion = true;
                
                DebuggerText += $"\n\n[AI SUGGESTION]\nDetected {unclassifiedCount} unclassified features.\n";
                DebuggerText += $"Suggest creating rule for layer '{mostCommonLayer}'.\nConfidence: 98%\nReason: Most frequent unmapped entity.\nClick 'Apply AI Suggestion' to accept.";
            }
            else
            {
                HasActiveAiSuggestion = false;
                _activeAiSuggestion = null;
            }
        });
    }
}
