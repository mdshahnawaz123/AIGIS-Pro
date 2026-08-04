using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AiGisConverter.Composition;
using AiGisConverter.Data.Context;
using AiGisConverter.Plugins.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Presentation.Startup;

/// <summary>What start-up managed to do.</summary>
/// <param name="DatabaseReady">Whether run history will be recorded this session.</param>
/// <param name="Plugins">Every plugin found, loaded or not.</param>
/// <param name="Warnings">Degradations the operator should know about.</param>
public sealed record StartupOutcome(
    bool DatabaseReady,
    IReadOnlyList<PluginDescriptor> Plugins,
    IReadOnlyList<string> Warnings);

/// <summary>
/// Prepares the application before the first window opens.
/// </summary>
/// <remarks>
/// Every step degrades rather than aborts. A workstation with an unwritable database or a broken
/// vendor plugin should still open and convert a DXF; the shell shows what is missing instead of
/// the application refusing to run. The one thing start-up will not do is hide a degradation.
/// </remarks>
public sealed class ApplicationStartup
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PluginBootstrapper _plugins;
    private readonly ILogger<ApplicationStartup> _logger;

    /// <summary>Initializes a new instance of the <see cref="ApplicationStartup"/> class.</summary>
    /// <param name="scopeFactory">Creates the scope the database initialiser needs.</param>
    /// <param name="plugins">Loads plugins and refreshes the caches that depend on them.</param>
    /// <param name="logger">Logger for start-up diagnostics.</param>
    public ApplicationStartup(
        IServiceScopeFactory scopeFactory,
        PluginBootstrapper plugins,
        ILogger<ApplicationStartup> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(plugins);
        ArgumentNullException.ThrowIfNull(logger);

        _scopeFactory = scopeFactory;
        _plugins = plugins;
        _logger = logger;
    }

    /// <summary>Runs start-up.</summary>
    /// <param name="cancellationToken">Token used to cancel start-up.</param>
    /// <returns>What start-up managed to do.</returns>
    public async Task<StartupOutcome> RunAsync(CancellationToken cancellationToken = default)
    {
        List<string> warnings = [];

        bool databaseReady = await PrepareDatabaseAsync(warnings, cancellationToken).ConfigureAwait(false);
        IReadOnlyList<PluginDescriptor> plugins = await LoadPluginsAsync(warnings, cancellationToken)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Start-up finished. Database {DatabaseState}, {LoadedCount} of {TotalCount} plugins loaded.",
            databaseReady ? "ready" : "unavailable",
            CountLoaded(plugins),
            plugins.Count);

        return new StartupOutcome(databaseReady, plugins, warnings);
    }

    private async Task<bool> PrepareDatabaseAsync(List<string> warnings, CancellationToken cancellationToken)
    {
        try
        {
            await using AsyncServiceScope scope = _scopeFactory.CreateAsyncScope();

            DatabaseInitialiser initialiser = scope.ServiceProvider.GetRequiredService<DatabaseInitialiser>();

            if (!await initialiser.InitialiseAsync(cancellationToken).ConfigureAwait(false))
            {
                warnings.Add(
                    "The history database is unavailable. Conversion works; run history and QA " +
                    "reports will not be recorded this session.");

                return false;
            }

            int pruned = await initialiser.PruneHistoryAsync(cancellationToken).ConfigureAwait(false);

            if (pruned > 0)
            {
                _logger.LogInformation("Pruned {Count} runs older than the retention window.", pruned);
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "The history database could not be prepared.");
            warnings.Add($"The history database could not be prepared: {ex.Message}");

            return false;
        }
    }

    private async Task<IReadOnlyList<PluginDescriptor>> LoadPluginsAsync(
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<PluginDescriptor> plugins =
                await _plugins.StartAsync(cancellationToken).ConfigureAwait(false);

            foreach (PluginDescriptor descriptor in plugins)
            {
                if (descriptor.State is PluginLoadState.Failed or PluginLoadState.Rejected)
                {
                    warnings.Add($"Plugin '{descriptor.Id}' did not load: {descriptor.FailureReason}");
                }
            }

            return plugins;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Plugin loading failed.");
            warnings.Add($"Plugins could not be loaded: {ex.Message}");

            return [];
        }
    }

    private static int CountLoaded(IReadOnlyList<PluginDescriptor> plugins)
    {
        int loaded = 0;

        foreach (PluginDescriptor descriptor in plugins)
        {
            if (descriptor.State == PluginLoadState.Loaded)
            {
                loaded++;
            }
        }

        return loaded;
    }
}
