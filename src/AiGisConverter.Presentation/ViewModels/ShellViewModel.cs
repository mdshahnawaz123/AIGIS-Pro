using System.Collections.ObjectModel;
using AiGisConverter.Application.Abstractions;
using AiGisConverter.Presentation.Services;
using AiGisConverter.Presentation.Startup;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AiGisConverter.MappingEditor.Presentation.ViewModels;

namespace AiGisConverter.Presentation.ViewModels;

/// <summary>
/// The main window: navigation, the notification list, and whatever start-up could not do.
/// </summary>
/// <remarks>
/// Start-up warnings are surfaced here rather than in a modal at launch. A workstation missing a
/// vendor plugin is a normal state that the operator should be able to see and dismiss, not an
/// interruption before they have done anything.
/// </remarks>
public sealed partial class ShellViewModel : ObservableObject
{
    private readonly INotificationService _notifications;
    private readonly IUiDispatcher _dispatcher;

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private string _title = "AI GIS Converter";

    [ObservableProperty]
    private bool _isDatabaseAvailable = true;

    [ObservableProperty]
    private bool _hasStartupWarnings;

    /// <summary>Initializes a new instance of the <see cref="ShellViewModel"/> class.</summary>
    /// <param name="project">The project page.</param>
    /// <param name="conversion">The conversion page.</param>
    /// <param name="qaQc">The quality page.</param>
    /// <param name="mappingEditor">The mapping editor page.</param>
    /// <param name="plugins">The plugins page.</param>
    /// <param name="settings">The settings page.</param>
    /// <param name="notifications">The notification source.</param>
    /// <param name="dispatcher">Marshals notifications onto the interface thread.</param>
    public ShellViewModel(
        ProjectViewModel project,
        ConversionViewModel conversion,
        QaQcViewModel qaQc,
        MappingEditorViewModel mappingEditor,
        PluginsViewModel plugins,
        SettingsViewModel settings,
        INotificationService notifications,
        IUiDispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(notifications);
        ArgumentNullException.ThrowIfNull(dispatcher);

        Project = project;
        Conversion = conversion;
        QaQc = qaQc;
        MappingEditor = mappingEditor;
        Plugins = plugins;
        Settings = settings;

        _notifications = notifications;
        _dispatcher = dispatcher;

        _currentPage = project;

        // Notifications are raised on whichever thread finished a conversion, and an observable
        // collection may only be touched by the thread that owns the view.
        _notifications.Published += (_, notification) =>
            _dispatcher.Post(() => Notifications.Insert(0, notification));
    }

    /// <summary>Gets the project page.</summary>
    public ProjectViewModel Project { get; }

    /// <summary>Gets the conversion page.</summary>
    public ConversionViewModel Conversion { get; }

    /// <summary>Gets the quality page.</summary>
    public QaQcViewModel QaQc { get; }

    /// <summary>Gets the mapping editor page.</summary>
    public MappingEditorViewModel MappingEditor { get; }

    /// <summary>Gets the plugins page.</summary>
    public PluginsViewModel Plugins { get; }

    /// <summary>Gets the settings page.</summary>
    public SettingsViewModel Settings { get; }

    /// <summary>Gets the notifications raised this session, newest first.</summary>
    public ObservableCollection<Notification> Notifications { get; } = [];

    /// <summary>Gets the things start-up could not do.</summary>
    public ObservableCollection<string> StartupWarnings { get; } = [];

    /// <summary>Records what start-up managed to do.</summary>
    /// <param name="outcome">The start-up outcome.</param>
    public void ApplyStartupOutcome(StartupOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);

        IsDatabaseAvailable = outcome.DatabaseReady;
        Plugins.Load(outcome.Plugins);

        foreach (string warning in outcome.Warnings)
        {
            StartupWarnings.Add(warning);
        }

        HasStartupWarnings = StartupWarnings.Count > 0;
    }

    /// <summary>Shows a page.</summary>
    /// <param name="page">The page to show.</param>
    [RelayCommand]
    private void Navigate(ObservableObject? page)
    {
        if (page is not null)
        {
            CurrentPage = page;
        }
    }

    /// <summary>Clears the notification list.</summary>
    [RelayCommand]
    private void ClearNotifications() => Notifications.Clear();

    /// <summary>Dismisses the start-up warnings.</summary>
    [RelayCommand]
    private void DismissWarnings()
    {
        StartupWarnings.Clear();
        HasStartupWarnings = false;
    }
}
