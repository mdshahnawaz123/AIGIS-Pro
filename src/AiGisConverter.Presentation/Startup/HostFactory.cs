using System;
using System.IO;
using AiGisConverter.Ai.DependencyInjection;
using AiGisConverter.Ai.Providers.Ollama;
using AiGisConverter.Ai.Providers.Onnx;
using AiGisConverter.Ai.Providers.OpenAi;
using AiGisConverter.Ai.Providers.RuleBased;
using AiGisConverter.Application.DependencyInjection;
using AiGisConverter.Cad.DependencyInjection;
using AiGisConverter.Composition;
using AiGisConverter.Data.DependencyInjection;
using AiGisConverter.Gis.DependencyInjection;
using AiGisConverter.Infrastructure.DependencyInjection;
using AiGisConverter.Infrastructure.Logging;
using AiGisConverter.Business.DependencyInjection;
using AiGisConverter.Plugins.Hosting.DependencyInjection;
using AiGisConverter.Presentation.Services;
using AiGisConverter.Presentation.ViewModels;
using AiGisConverter.QaQc.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Serilog;
using AiGisConverter.MappingEditor.Presentation.ViewModels;
using AiGisConverter.MappingEditor.Application;
using AiGisConverter.MappingEditor.Business;
using Serilog.Extensions.Logging;

namespace AiGisConverter.Presentation.Startup;

/// <summary>
/// The composition root: the one place every layer meets.
/// </summary>
/// <remarks>
/// <para>
/// Ordering matters in exactly two places and nowhere else. The AI layer must be registered before
/// the plugin integration that adds a second provider source to it, and the plugin system must be
/// registered before the integration that reads its capability registry. Everything else is
/// order-independent because each layer registers only its own contracts.
/// </para>
/// <para>
/// No layer registers another. <c>AddGisLayer</c> knows nothing about CAD; <c>AddApplicationLayer</c>
/// knows nothing about either. The knowledge that they are used together lives here and only here.
/// </para>
/// </remarks>
public static class HostFactory
{
    /// <summary>Builds the application host.</summary>
    /// <returns>The host, not yet started.</returns>
    public static IHost Create()
    {
        HostApplicationBuilder builder = Host.CreateApplicationBuilder();

        builder.Configuration
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile("appsettings.Development.json", optional: true, reloadOnChange: true)
            .AddJsonFile(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AiGisConverter",
                    "appsettings.Local.json"),
                optional: true,
                reloadOnChange: true)
            .AddEnvironmentVariables("AIGIS_");

        ConfigureLogging(builder);
        ConfigureLayers(builder.Services, builder.Configuration);
        ConfigurePresentation(builder.Services);

        return builder.Build();
    }

    private static void ConfigureLogging(HostApplicationBuilder builder)
    {
        Serilog.ILogger logger = SerilogConfigurator.Create(builder.Configuration);

        builder.Services.AddSingleton(logger);
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(new SerilogLoggerProvider(logger, dispose: false));
    }

    /// <summary>Registers the nine layers.</summary>
    private static void ConfigureLayers(IServiceCollection services, IConfiguration configuration)
    {
        services.AddInfrastructureLayer(configuration);
        services.AddDataLayer(configuration);

        services.AddCadLayer(configuration);
        services.AddGisLayer(configuration);
        services.AddQaQcLayer(configuration);

        services.AddAiLayer(configuration, providers => providers
            .AddRuleBasedProvider()
            .AddOnnxProvider()
            .AddOllamaProvider()
            .AddOpenAiProvider());

        services.AddPluginSystem(configuration);

        // Must follow the AI layer and the plugin system: it adds a capability-backed provider
        // source to the former and reads the registry of the latter.
        services.AddPluginIntegration();

        services.AddAiGisBusiness();
        services.AddApplicationLayer();
    }

    private static void ConfigurePresentation(IServiceCollection services)
    {
        services.AddSingleton<IUiDispatcher, WpfUiDispatcher>();
        services.AddSingleton<IDialogService, WpfDialogService>();
        services.AddSingleton<ApplicationStartup>();

        services.AddSingleton<ShellViewModel>();
        services.AddSingleton<ProjectViewModel>();
        services.AddSingleton<ConversionViewModel>();
        services.AddSingleton<QaQcViewModel>();
        services.AddSingleton<PluginsViewModel>();
        services.AddSingleton<SettingsViewModel>();

        // Mapping Editor
        services.AddSingleton<MappingEditorViewModel>();
        services.AddSingleton<IMappingEditorService, MappingEditorService>();
        services.AddSingleton<RuleSimulator>();
        services.AddSingleton<RuleValidator>();
        services.AddSingleton<LiveSimulationService>();
        services.AddSingleton<StatisticsService>();
    }
}
