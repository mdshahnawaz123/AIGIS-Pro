using System.Collections.ObjectModel;
using AiGisConverter.Plugins.Hosting;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AiGisConverter.Presentation.ViewModels;

/// <summary>
/// What plugins were found, and what became of them.
/// </summary>
/// <remarks>
/// Rejected and failed plugins are shown alongside loaded ones. A plugin that silently does not
/// appear is the hardest kind of problem to diagnose, so discovery deliberately keeps the ones it
/// refused and this page deliberately shows them.
/// </remarks>
public sealed partial class PluginsViewModel : ObservableObject
{
    [ObservableProperty]
    private string _summary = "No plugins have been discovered.";

    /// <summary>Gets every plugin discovered, loaded or not.</summary>
    public ObservableCollection<PluginRowViewModel> Plugins { get; } = [];

    /// <summary>Records what the plugin host found.</summary>
    /// <param name="descriptors">Every plugin discovered.</param>
    public void Load(IReadOnlyList<PluginDescriptor> descriptors)
    {
        ArgumentNullException.ThrowIfNull(descriptors);

        Plugins.Clear();

        foreach (PluginDescriptor descriptor in descriptors
            .OrderBy(static d => d.Manifest.Name, StringComparer.OrdinalIgnoreCase))
        {
            Plugins.Add(new PluginRowViewModel(descriptor));
        }

        int loaded = descriptors.Count(static d => d.State == PluginLoadState.Loaded);

        Summary = descriptors.Count == 0
            ? "No plugins were found in the configured search paths."
            : $"{loaded} of {descriptors.Count} plugins loaded.";
    }
}

/// <summary>One plugin, flattened for the list.</summary>
public sealed class PluginRowViewModel
{
    /// <summary>Initializes a new instance of the <see cref="PluginRowViewModel"/> class.</summary>
    /// <param name="descriptor">The discovered plugin.</param>
    public PluginRowViewModel(PluginDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        Id = descriptor.Id;
        Name = descriptor.Manifest.Name;
        Version = descriptor.Manifest.Version;
        Publisher = descriptor.Manifest.Publisher ?? "Unknown";
        Capabilities = string.Join(", ", descriptor.Manifest.Capabilities);
        State = descriptor.State.ToString();
        IsLoaded = descriptor.State == PluginLoadState.Loaded;
        Detail = descriptor.FailureReason
                 ?? descriptor.Manifest.Description
                 ?? string.Empty;

        RequiresHost = descriptor.Manifest.HostApplication?.Name;
    }

    /// <summary>Gets the plugin identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the display name.</summary>
    public string Name { get; }

    /// <summary>Gets the plugin version.</summary>
    public string Version { get; }

    /// <summary>Gets the publisher.</summary>
    public string Publisher { get; }

    /// <summary>Gets the capabilities the manifest advertises.</summary>
    public string Capabilities { get; }

    /// <summary>Gets the lifecycle state.</summary>
    public string State { get; }

    /// <summary>Gets a value indicating whether the plugin is in use.</summary>
    public bool IsLoaded { get; }

    /// <summary>Gets the failure reason, or the description when there is none.</summary>
    public string Detail { get; }

    /// <summary>Gets the host application the plugin needs, when it needs one.</summary>
    public string? RequiresHost { get; }
}
