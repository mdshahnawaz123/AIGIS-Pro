using AiGisConverter.Ai.Abstractions;
using AiGisConverter.Ai.Features;
using AiGisConverter.Ai.Prompting;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.AiProviders;

/// <summary>
/// Contributes one <see cref="IAIProvider"/> for every OpenAI-compatible endpoint in configuration.
/// </summary>
/// <remarks>
/// <para>
/// This plugin is the proof that the AI layer is genuinely open. Adding an LM Studio or Azure
/// OpenAI provider required no change to <c>AiGisConverter.Ai</c>: the provider is built here,
/// registered as a capability, and picked up by the factory through
/// <c>CapabilityAIProviderSource</c>.
/// </para>
/// <para>
/// The prompt builder and response parser are constructed locally rather than resolved from the
/// host container. A plugin owns its dependencies; borrowing the host's would couple its lifetime
/// to the host's and prevent the load context from ever being collected.
/// </para>
/// </remarks>
public sealed class AiProvidersPlugin : PluginBase
{
    private readonly List<OpenAiCompatibleProvider> _providers = [];

    /// <inheritdoc />
    public override string Id => "aigis.ai.providers";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        IPluginContext context = registration.Context;

        AiProvidersPluginOptions options = new();
        context.Configuration.GetSection("endpoints").Bind(options.Endpoints);

        if (options.Endpoints.Count == 0)
        {
            context.Logger.LogInformation(
                "No endpoints are configured under Plugins:{PluginId}:endpoints, so no AI providers " +
                "were contributed.",
                Id);

            return Task.CompletedTask;
        }

        IChatPromptBuilder promptBuilder = new ClassificationPromptBuilder(new SubjectDescriptor());
        IClassificationResponseParser parser = new JsonClassificationResponseParser(
            context.LoggerFactory.CreateLogger<JsonClassificationResponseParser>());

        foreach (OpenAiCompatibleEndpointOptions endpoint in options.Endpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint.Key) || string.IsNullOrWhiteSpace(endpoint.Model))
            {
                context.Logger.LogWarning(
                    "Skipped an endpoint with no 'key' or no 'model'. Both are required.");
                continue;
            }

            // The shipped appsettings.json documents an Azure endpoint with '<resource>' and
            // '<deployment>' left as placeholders for the operator to fill in. Those are not a
            // valid host name, so constructing the provider threw - and the throw escaped far
            // enough to abort loading every other plugin. An endpoint nobody has configured yet is
            // a normal state, not a fault, and it belongs skipped with a note rather than fatal.
            if (!Uri.TryCreate(endpoint.BaseAddress, UriKind.Absolute, out Uri? baseAddress)
                || (baseAddress.Scheme != Uri.UriSchemeHttp && baseAddress.Scheme != Uri.UriSchemeHttps))
            {
                context.Logger.LogWarning(
                    "Skipped AI endpoint '{ProviderKey}': '{BaseAddress}' is not an absolute http or https "
                    + "address. Replace the placeholders in Plugins:{PluginId}:endpoints to enable it.",
                    endpoint.Key,
                    endpoint.BaseAddress,
                    Id);

                continue;
            }

            OpenAiCompatibleProvider provider = new(
                endpoint,
                promptBuilder,
                parser,
                context.LoggerFactory.CreateLogger($"AiProvider.{endpoint.Key}"));

            _providers.Add(provider);
            registration.AddCapability<IAIProvider>(provider);

            context.Logger.LogInformation(
                "Contributed AI provider '{ProviderKey}' targeting {BaseAddress} ({Model}).",
                endpoint.Key,
                endpoint.BaseAddress,
                endpoint.Model);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public override Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        // Every HttpClient must be released, or its handler pins this plugin's load context and
        // the assemblies stay mapped for the life of the process.
        foreach (OpenAiCompatibleProvider provider in _providers)
        {
            provider.Dispose();
        }

        _providers.Clear();

        return Task.CompletedTask;
    }
}
