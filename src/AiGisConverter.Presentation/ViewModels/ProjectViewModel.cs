using System.Collections.ObjectModel;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Gis.Abstractions;
using AiGisConverter.Presentation.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiGisConverter.Presentation.ViewModels;

/// <summary>Building the set of drawings to convert, and the settings they convert under.</summary>
public sealed partial class ProjectViewModel : ObservableObject, IDisposable
{
    private readonly IDataSourceReaderCatalog _readers;
    private readonly IDialogService _dialogs;
    private readonly ICrsSuggester _suggester;
    private readonly ICrsPreferences _preferences;
    private readonly ICrsValidator _validator;
    private readonly ICoordinateTransformer _transformer;

    [ObservableProperty]
    private bool _isPassThrough;

    // The drawing's own extent and units, captured when a drawing is added, so validation can run
    // before conversion without re-reading the file.
    private Extent _detectedExtent = Extent.Empty;
    private string? _detectedUnits;

    [ObservableProperty]
    private ObservableCollection<CrsValidationFinding> _validationFindings = [];

    [ObservableProperty]
    private string _validationSummary = string.Empty;

    [ObservableProperty]
    private bool _hasValidationErrors;

    [ObservableProperty]
    private string _projectName = "Untitled project";

    [ObservableProperty]
    private ExportFormat _exportFormat = ExportFormat.GeoJson;

    [ObservableProperty]
    private string? _outputFolder;

    [ObservableProperty]
    private double _confidenceThreshold = 0.65d;

    /// <summary>Initializes a new instance of the <see cref="ProjectViewModel"/> class.</summary>
    /// <param name="readers">Supplies the extensions the file dialog should offer.</param>
    /// <param name="dialogs">Shows the file pickers.</param>
    /// <param name="crsCatalog">The EPSG/PROJ catalogue driving the CRS selectors.</param>
    /// <param name="suggester">Detects the input coordinate system from a drawing.</param>
    /// <param name="preferences">Remembers recent and favourite coordinate systems.</param>
    /// <param name="validator">Runs the pre-conversion coordinate-system checks.</param>
    /// <param name="transformer">Used only to ask whether a reprojection can be performed.</param>
    public ProjectViewModel(
        IDataSourceReaderCatalog readers,
        IDialogService dialogs,
        ICrsCatalog crsCatalog,
        ICrsSuggester suggester,
        ICrsPreferences preferences,
        ICrsValidator validator,
        ICoordinateTransformer transformer)
    {
        ArgumentNullException.ThrowIfNull(readers);
        ArgumentNullException.ThrowIfNull(dialogs);
        ArgumentNullException.ThrowIfNull(crsCatalog);
        ArgumentNullException.ThrowIfNull(suggester);
        ArgumentNullException.ThrowIfNull(preferences);
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(transformer);

        _transformer = transformer;
        _readers = readers;
        _dialogs = dialogs;
        _suggester = suggester;
        _preferences = preferences;
        _validator = validator;

        // Start on what this operator used last, falling back to the project or company default.
        string initialOutput =
            preferences.LastOutput
            ?? preferences.ProjectDefault
            ?? preferences.CompanyDefault
            ?? "EPSG:4326";

        InputCrsSelector = new CrsSelectorViewModel(
            crsCatalog, preferences, "Input coordinate system", preferences.LastInput ?? "Auto-detect");
        OutputCrsSelector = new CrsSelectorViewModel(
            crsCatalog, preferences, "Output coordinate system", initialOutput);
    }

    /// <summary>Gets the picker for the drawing's own coordinate system (blank = auto-detect).</summary>
    public CrsSelectorViewModel InputCrsSelector { get; }

    /// <summary>Gets the picker for the coordinate system to convert into.</summary>
    public CrsSelectorViewModel OutputCrsSelector { get; }

    /// <summary>Gets the drawings queued for conversion.</summary>
    public ObservableCollection<string> Drawings { get; } = [];

    /// <summary>Gets the export formats a project may target.</summary>
    public IReadOnlyList<ExportFormat> AvailableFormats { get; } =
    [
        ExportFormat.GeoJson,
        ExportFormat.GeoPackage,
        ExportFormat.Shapefile,
        ExportFormat.Kml,
        ExportFormat.Csv,
    ];

    /// <summary>Gets the extensions any registered reader accepts.</summary>
    public IReadOnlyList<string> SupportedExtensions => _readers.GetSupportedExtensions();

    /// <summary>Gets the formatted extensions for display.</summary>
    public string SupportedExtensionsText => string.Join(", ", _readers.GetSupportedExtensions());

    /// <summary>Gets a value indicating whether the project can be converted.</summary>
    public bool CanConvert =>
        Drawings.Count > 0
        && !string.IsNullOrWhiteSpace(OutputFolder)
        && !HasValidationErrors;

    /// <summary>Adds drawings.</summary>
    [RelayCommand]
    private void AddDrawings()
    {
        foreach (string path in _dialogs.PickDrawings(SupportedExtensions))
        {
            if (!Drawings.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Drawings.Add(path);
            }
        }

        OnPropertyChanged(nameof(CanConvert));

        // Detect the input CRS from the first drawing, in the background. Suggest-then-confirm:
        // it fills the Input selector but the operator can still override it.
        if (Drawings.Count > 0)
        {
            _ = DetectInputCrsAsync(Drawings[0]);
        }
    }

    /// <summary>
    /// Reads a drawing and offers the most likely input coordinate system.
    /// </summary>
    /// <remarks>
    /// Runs off the command so a large drawing does not freeze the form. Detection only suggests:
    /// a confident result is applied to the Input selector, and every result — confident or not —
    /// is explained in the selector's detection message so the operator can judge it.
    /// </remarks>
    /// <param name="path">The drawing to inspect.</param>
    private async Task DetectInputCrsAsync(string path)
    {
        try
        {
            InputCrsSelector.DetectionMessage = "Detecting coordinate system...";

            SourceReference reference = new(path);
            IDataSourceReader? reader = _readers.FindReader(reference);

            if (reader is null)
            {
                InputCrsSelector.DetectionMessage = "No reader can open this drawing.";
                return;
            }

            Result<SourceDocument> read = await reader.ReadAsync(reference).ConfigureAwait(true);

            if (read.IsFailure)
            {
                InputCrsSelector.DetectionMessage = $"Could not read the drawing: {read.Error.Message}";
                return;
            }

            Extent extent = ExtentOf(read.Value);

            // Keep the drawing's shape so validation can run later without re-reading the file.
            _detectedExtent = extent;
            _detectedUnits = read.Value.Units;

            IReadOnlyList<CrsSuggestion> suggestions =
                await _suggester.SuggestAsync(extent, read.Value.DeclaredCrs).ConfigureAwait(true);

            await ApplyDetectionAsync(suggestions).ConfigureAwait(true);
            await RunValidationAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            InputCrsSelector.DetectionMessage = $"Detection failed: {ex.Message}";
        }
    }

    /// <summary>Applies a ranked detection to the Input selector.</summary>
    /// <remarks>
    /// A confident result (a declared CRS, or plain longitude/latitude) is applied outright. An
    /// ambiguous UTM drawing instead lists the candidate zones — each labelled with the region it
    /// would imply — as clickable results, because the numbers alone cannot single out the zone and
    /// silently choosing one risks placing the drawing in the wrong country.
    /// </remarks>
    private async Task ApplyDetectionAsync(IReadOnlyList<CrsSuggestion> suggestions)
    {
        if (suggestions.Count == 0)
        {
            InputCrsSelector.DetectionMessage = "Unable to confidently determine CRS.";
            return;
        }

        CrsSuggestion top = suggestions[0];
        string ranked = string.Join("\n", suggestions.Select(static s => $"  {s.Label} — {s.Reason}"));

        if (top.IsConfident && top.CoordinateSystem is not null)
        {
            InputCrsSelector.SelectedIdentifier = top.CoordinateSystem.Identifier;
            InputCrsSelector.DetectionMessage = $"Auto-detected {top.Label}.\n{ranked}";
            return;
        }

        // Ambiguous: show the candidate zones as clickable results and ask the operator to confirm.
        IReadOnlyList<string> candidates =
            [.. suggestions.Where(static s => s.CoordinateSystem is not null).Select(static s => s.CoordinateSystem!.Identifier)];

        if (candidates.Count > 0)
        {
            await InputCrsSelector.ShowSuggestedAsync(candidates).ConfigureAwait(true);
        }

        InputCrsSelector.DetectionMessage =
            "Could not determine the CRS from coordinates alone — pick the region below:\n" + ranked;
    }

    /// <summary>
    /// Runs the coordinate-system checks and publishes them for the Project page.
    /// </summary>
    /// <remarks>
    /// Re-run whenever the drawing or either system changes, so the operator sees the consequence
    /// of a choice immediately rather than at the moment they press Convert.
    /// </remarks>
    /// <returns>A task that completes when the checks have run.</returns>
    [RelayCommand]
    private async Task ValidateAsync() => await RunValidationAsync().ConfigureAwait(true);

    /// <summary>Runs the checks and returns the report.</summary>
    /// <returns>The report, so callers can decide whether to proceed.</returns>
    public async Task<CrsValidationReport> RunValidationAsync()
    {
        // "Auto-detect" is a deliberate non-identifier: a failed parse means "not chosen yet",
        // which the validator reports as a missing selection.
        CoordinateSystem? input =
            CoordinateSystem.TryParse(InputCrsSelector.SelectedIdentifier, out CoordinateSystem? parsedInput)
                ? parsedInput
                : null;

        CoordinateSystem? output =
            CoordinateSystem.TryParse(OutputCrsSelector.SelectedIdentifier, out CoordinateSystem? parsedOutput)
                ? parsedOutput
                : null;

        CrsValidationReport report = await _validator
            .ValidateAsync(new CrsValidationRequest(input, output, _detectedExtent, _detectedUnits))
            .ConfigureAwait(true);

        ValidationFindings = new ObservableCollection<CrsValidationFinding>(report.Findings);
        ValidationSummary = report.Summary;
        HasValidationErrors = report.HasErrors;

        OnPropertyChanged(nameof(CanConvert));

        return report;
    }

    /// <summary>Computes a drawing's extent from its element geometry.</summary>
    private static Extent ExtentOf(SourceDocument document)
    {
        Extent extent = Extent.Empty;

        foreach (SourceLayer layer in document.Layers)
        {
            foreach (SourceElement element in layer.Elements)
            {
                if (element.Geometry is { IsEmpty: false } geometry)
                {
                    NetTopologySuite.Geometries.Envelope box = geometry.EnvelopeInternal;
                    extent = extent.Union(Extent.Create(box.MinX, box.MinY, box.MaxX, box.MaxY));
                }
            }
        }

        return extent;
    }

    /// <summary>Removes a drawing.</summary>
    /// <param name="path">The drawing to remove.</param>
    [RelayCommand]
    private void RemoveDrawing(string? path)
    {
        if (path is not null)
        {
            Drawings.Remove(path);
            OnPropertyChanged(nameof(CanConvert));
        }
    }

    /// <summary>Chooses the output folder.</summary>
    [RelayCommand]
    private void ChooseOutputFolder()
    {
        string? chosen = _dialogs.PickOutputFolder(OutputFolder);

        if (chosen is not null)
        {
            OutputFolder = chosen;
            OnPropertyChanged(nameof(CanConvert));
        }
    }

    /// <summary>
    /// Builds the domain aggregate from what the operator has entered.
    /// </summary>
    /// <remarks>
    /// The view model holds text and the domain holds rules. Everything typed here passes through
    /// the domain's factories, so an unparseable coordinate system or an empty project is rejected
    /// by the same code that would reject it from a script.
    /// </remarks>
    /// <returns>The project, ready to convert.</returns>
    public ConversionProject BuildProject()
    {
        CoordinateSystem target = CoordinateSystem.Parse(OutputCrsSelector.SelectedIdentifier);

        CoordinateSystem? source =
            CoordinateSystem.TryParse(InputCrsSelector.SelectedIdentifier, out CoordinateSystem? parsedSource)
                ? parsedSource
                : null;

        // Pass-through mode. When the machine cannot perform the reprojection at all, asking for
        // one would fail every feature and produce an empty dataset. Converting in the drawing's
        // own coordinates keeps the entire rest of the pipeline - classification, QA, export, the
        // editor - working, and the operator is told plainly that the output is not reprojected.
        IsPassThrough = source is not null && source != target && !_transformer.CanTransform(source, target);

        if (IsPassThrough)
        {
            target = source!;
        }

        ConversionSettings settings = ConversionSettings
            .Create(target, [ExportFormat])
            .WithConfidenceThreshold(ConfidenceThreshold);

        // When the operator has chosen an explicit Input CRS (rather than leaving it on
        // "Auto-detect"), it overrides the pipeline's own detection. "Auto-detect" is not a valid
        // identifier, so it simply does not parse and detection runs as before.
        if (source is not null)
        {
            settings = settings with { AssumedSourceCoordinateSystem = source };
            _preferences.LastInput = InputCrsSelector.SelectedIdentifier;
            InputCrsSelector.RememberSelection();
        }

        // Remember what was actually converted with, so the next session starts where this ended.
        _preferences.LastOutput = OutputCrsSelector.SelectedIdentifier;
        OutputCrsSelector.RememberSelection();
        _preferences.Save();

        ConversionProject project = ConversionProject.Create(ProjectName, settings);

        foreach (string drawing in Drawings)
        {
            project.AddJob(new SourceReference(drawing));
        }

        return project;
    }

    /// <summary>Releases the coordinate-system selectors.</summary>
    public void Dispose()
    {
        InputCrsSelector.Dispose();
        OutputCrsSelector.Dispose();
    }
}
