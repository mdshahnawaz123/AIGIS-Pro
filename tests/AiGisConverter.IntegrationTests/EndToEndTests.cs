using AiGisConverter.Application.Abstractions;
using AiGisConverter.Application.Pipelines;
using AiGisConverter.Cad.DependencyInjection;
using AiGisConverter.Data.DependencyInjection;
using AiGisConverter.Gis.DependencyInjection;
using AiGisConverter.Infrastructure.DependencyInjection;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.Enums;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using AiGisConverter.Business.DependencyInjection;
using FluentAssertions;
using AiGisConverter.Application.DependencyInjection;
using AiGisConverter.QaQc.DependencyInjection;
using AiGisConverter.Domain.ValueObjects;
using AiGisConverter.Ai.DependencyInjection;
using AiGisConverter.Ai.Providers.Ollama;
using AiGisConverter.Ai.Providers.Onnx;
using AiGisConverter.Ai.Providers.OpenAi;
using AiGisConverter.Ai.Providers.RuleBased;
using AiGisConverter.Plugins.Hosting.DependencyInjection;
using AiGisConverter.Composition;

namespace AiGisConverter.IntegrationTests;

public sealed class EndToEndTests
{
    [Fact]
    public async Task Pipeline_ConvertsDxfToGeoJson()
    {
        // 1. Setup DI
        IConfiguration config = new ConfigurationBuilder().Build();
        ServiceCollection services = new();
        
        services.AddLogging(b => b.ClearProviders());
        services.AddSingleton<ILoggerFactory, NullLoggerFactory>();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        
        services.AddInfrastructureLayer(config);
        services.AddDataLayer(config);
        services.AddCadLayer(config);
        services.AddGisLayer(config);
        services.AddQaQcLayer(config);
        services.AddAiGisBusiness();
        
        services.AddAiLayer(config, providers => providers
            .AddRuleBasedProvider()
            .AddOnnxProvider()
            .AddOllamaProvider()
            .AddOpenAiProvider());

        services.AddPluginSystem(config);
        services.AddPluginIntegration();
        
        services.AddApplicationLayer();

        ServiceProvider provider = services.BuildServiceProvider();

        // 2. Setup job
        string sourcePath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "samples", "sample.dxf"));
        string outDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "out_e2e"));
        Directory.CreateDirectory(outDir);

        ConversionSettings settings = ConversionSettings.Default();
        // Just geojson
        var newFormats = new List<ExportFormat> { ExportFormat.GeoJson };
        var settingsField = typeof(ConversionSettings).GetField("_exportFormats", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        settingsField?.SetValue(settings, newFormats);

        ConversionProject project = ConversionProject.Create("Test", settings);
        ConversionJob job = project.AddJob(new SourceReference(sourcePath));
        ConversionRun run = ConversionRun.Create(job, project.Settings);
        run.Start();

        PipelineContext context = new(job.Source, project.Settings, run, outDir);

        // 3. Run
        IConversionPipeline pipeline = provider.GetRequiredService<IConversionPipeline>();
        Result result = await pipeline.ExecuteAsync(context);

        // 4. Assert
        result.IsSuccess.Should().BeTrue(result.Error?.Message ?? "No error message");
        run.OutputPaths.Should().NotBeEmpty();
        
        string geoJsonPath = run.OutputPaths.First();
        File.Exists(geoJsonPath).Should().BeTrue();
        
        string geoJsonText = await File.ReadAllTextAsync(geoJsonPath);
        geoJsonText.Should().Contain("FeatureCollection");

        Console.WriteLine("---- VALIDATION INFO ----");
        var qaReport = context.Report;
        Console.WriteLine($"Total Issues: {qaReport?.Issues.Count ?? 0}");
        if (qaReport != null)
        {
            foreach (var issue in qaReport.Issues)
            {
                Console.WriteLine($"[{issue.Severity}] {issue.Category}: {issue.Message}");
            }
        }
        
        int pointCount = geoJsonText.Split("\"type\":\"Point\"").Length - 1;
        int lineCount = geoJsonText.Split("\"type\":\"LineString\"").Length - 1;
        int polyCount = geoJsonText.Split("\"type\":\"Polygon\"").Length - 1;
        Console.WriteLine($"GeoJSON File Size: {geoJsonText.Length} bytes");
        Console.WriteLine($"GeoJSON Points: {pointCount}, Lines: {lineCount}, Polygons: {polyCount}");

        Console.WriteLine("Source Document Element count: " + (context.Document?.Layers.Sum(l => l.Elements.Count) ?? 0));
        var classifications = context.EntityClassifications;
        Console.WriteLine("Classified entities: " + classifications.Count);
        
        var groups = classifications.Values.GroupBy(c => c.Label);
        foreach (var group in groups)
        {
            Console.WriteLine($" - {group.Key}: {group.Count()}");
        }
    }
}
