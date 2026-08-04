using System.Collections.ObjectModel;
using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Exceptions;
using AiGisConverter.Ai.Models;
using AiGisConverter.Gis.Profiles;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AiGisConverter.Presentation.ViewModels;

/// <summary>
/// What is configured, and what is actually available.
/// </summary>
/// <remarks>
/// The page reports rather than edits. Settings live in <c>appsettings.json</c> and in conversion
/// profiles, both of which are edited as files and reloaded; a second editing surface in the
/// application would be a second source of truth that drifts from the first.
/// </remarks>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly IAIProviderFactory _providers;
    private readonly IProfileRepository _profiles;

    [ObservableProperty]
    private string _activeProvider = "(resolving)";

    [ObservableProperty]
    private string _providerStatus = "Not checked.";

    [ObservableProperty]
    private bool _isProbing;

    /// <summary>Initializes a new instance of the <see cref="SettingsViewModel"/> class.</summary>
    /// <param name="providers">Resolves the configured AI provider.</param>
    /// <param name="profiles">Supplies the conversion profiles.</param>
    public SettingsViewModel(IAIProviderFactory providers, IProfileRepository profiles)
    {
        ArgumentNullException.ThrowIfNull(providers);
        ArgumentNullException.ThrowIfNull(profiles);

        _providers = providers;
        _profiles = profiles;

        Refresh();
    }

    /// <summary>Gets the AI providers registered, built in or contributed by a plugin.</summary>
    public ObservableCollection<AIProviderMetadata> Providers { get; } = [];

    /// <summary>Gets the conversion profiles available.</summary>
    public ObservableCollection<ConversionProfile> Profiles { get; } = [];

    /// <summary>Reloads what is registered.</summary>
    [RelayCommand]
    private void Refresh()
    {
        Providers.Clear();
        Profiles.Clear();

        foreach (AIProviderMetadata metadata in _providers.GetRegisteredProviders())
        {
            Providers.Add(metadata);
        }

        foreach (ConversionProfile profile in _profiles.GetAll())
        {
            Profiles.Add(profile);
        }

        try
        {
            ActiveProvider = _providers.GetActiveProvider().Key;
        }
        catch (AIProviderNotRegisteredException ex)
        {
            ActiveProvider = "(none)";
            ProviderStatus = ex.Message;
        }
    }

    /// <summary>
    /// Asks the active provider whether it can actually be used.
    /// </summary>
    /// <remarks>
    /// Configuration naming a provider says nothing about whether the endpoint answers or the
    /// model file exists, which is exactly what a user wants to know before starting a batch.
    /// </remarks>
    [RelayCommand]
    private async Task ProbeAsync()
    {
        IsProbing = true;
        ProviderStatus = "Checking...";

        try
        {
            IAIProvider provider = _providers.GetActiveProvider();
            AIProviderAvailability availability = await provider.ProbeAsync().ConfigureAwait(true);

            ProviderStatus = availability.IsAvailable
                ? $"{provider.Key} is available ({availability.ModelIdentifier ?? "model unspecified"})."
                : $"{provider.Key} is unavailable: {availability.Reason}";
        }
        catch (Exception ex)
        {
            ProviderStatus = $"The provider could not be checked: {ex.Message}";
        }
        finally
        {
            IsProbing = false;
        }
    }
}
