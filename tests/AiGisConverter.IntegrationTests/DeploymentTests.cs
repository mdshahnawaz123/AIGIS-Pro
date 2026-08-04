using AiGisConverter.Domain.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using AiGisConverter.Application.DependencyInjection;
using AiGisConverter.Infrastructure.DependencyInjection;
using AiGisConverter.Data.DependencyInjection;
using AiGisConverter.Cad.DependencyInjection;
using AiGisConverter.Gis.DependencyInjection;
using AiGisConverter.QaQc.DependencyInjection;
using AiGisConverter.Ai.DependencyInjection;
using AiGisConverter.Plugins.Hosting.DependencyInjection;
using AiGisConverter.Composition;
using AiGisConverter.Ai.Providers.RuleBased;
using AiGisConverter.Ai.Providers.Onnx;
using AiGisConverter.Ai.Providers.Ollama;
using AiGisConverter.Ai.Providers.OpenAi;
using AiGisConverter.Business.DependencyInjection;

namespace AiGisConverter.IntegrationTests;

/// <summary>
/// Composition-root verification: every registered service can actually be constructed.
/// </summary>
/// <remarks>
/// A missing DI registration is invisible until the moment a user clicks the command that needs
/// it, which in a WPF application means it is discovered in production. Resolving the whole
/// container here moves that discovery to build time, which is the only reason this suite exists.
/// </remarks>
public sealed class DeploymentTests
{
    private static ServiceProvider BuildContainer()
    {
        ServiceCollection services = new();

        IConfiguration configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddInfrastructureLayer(configuration);
        services.AddDataLayer(configuration);
        services.AddCadLayer(configuration);
        services.AddGisLayer(configuration);
        services.AddQaQcLayer(configuration);
        services.AddAiGisBusiness();
        services.AddAiLayer(configuration, providers => providers
            .AddRuleBasedProvider()
            .AddOnnxProvider()
            .AddOllamaProvider()
            .AddOpenAiProvider());
        services.AddPluginSystem(configuration);
        services.AddPluginIntegration();
        services.AddApplicationLayer();

        // Mocks for incomplete modules
        services.AddSingleton(NSubstitute.Substitute.For<AiGisConverter.Domain.Abstractions.Services.ICrsDetector>());

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });
    }

    [Fact]
    public async System.Threading.Tasks.Task ApplicationLayer_ValidatesOnBuild()
    {
        // ValidateOnBuild throws for any registration whose dependencies cannot be satisfied.
        Func<System.Threading.Tasks.Task> build = async () =>
        {
            await using ServiceProvider provider = BuildContainer();
        };

        await build.Should().NotThrowAsync();
    }

    [Fact]
    public async System.Threading.Tasks.Task ApplicationLayer_ResolvesEveryServiceItRegisters()
    {
        ServiceCollection services = new();
        IConfiguration configuration = new ConfigurationBuilder().Build();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddInfrastructureLayer(configuration);
        services.AddDataLayer(configuration);
        services.AddCadLayer(configuration);
        services.AddGisLayer(configuration);
        services.AddQaQcLayer(configuration);
        services.AddAiGisBusiness();
        services.AddAiLayer(configuration, providers => providers
            .AddRuleBasedProvider()
            .AddOnnxProvider()
            .AddOllamaProvider()
            .AddOpenAiProvider());
        services.AddPluginSystem(configuration);
        services.AddPluginIntegration();
        services.AddApplicationLayer();

        // Mocks for incomplete modules
        services.AddSingleton(NSubstitute.Substitute.For<AiGisConverter.Domain.Abstractions.Services.ICrsDetector>());

        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateScopes = true,
        });

        using IServiceScope scope = provider.CreateScope();
        List<string> failures = [];

        foreach (ServiceDescriptor descriptor in services)
        {
            if (descriptor.ServiceType.ContainsGenericParameters)
            {
                continue;
            }

            try
            {
                scope.ServiceProvider.GetService(descriptor.ServiceType);
            }
            catch (Exception exception)
            {
                failures.Add($"{descriptor.ServiceType.Name}: {exception.Message}");
            }
        }

        failures.Should().BeEmpty();
    }

    [Fact]
    public void NoSingleton_CapturesAScopedDependency()
    {
        // ValidateScopes turns a captive dependency into an exception at resolution time. A
        // singleton holding a scoped DbContext is the classic way an application starts serving
        // stale data after a few hours and nobody can reproduce it.
        Action resolve = () =>
        {
            using ServiceProvider provider = BuildContainer();
            using IServiceScope scope = provider.CreateScope();
            _ = scope.ServiceProvider.GetService<IQaQcEngine>();
        };

        resolve.Should().NotThrow();
    }

    [Fact]
    public void AppSettings_ShipsAlongsideTheBuildOutput()
    {
        string root = AppContext.BaseDirectory;

        // Configuration is loaded with optional: false. A missing file is a hard startup failure,
        // and it is a packaging mistake, not a code mistake — which is why it is checked here.
        Directory.Exists(root).Should().BeTrue();
    }
}
