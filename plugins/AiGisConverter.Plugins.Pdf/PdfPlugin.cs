using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Plugins.Abstractions;

namespace AiGisConverter.Plugins.Pdf;

/// <summary>
/// Contributes the PDF Vector Reader to the host.
/// </summary>
public sealed class PdfPlugin : PluginBase, IPluginHealthCheck
{
    /// <inheritdoc />
    public override string Id => "aigis.reader.pdf";

    /// <inheritdoc />
    protected override Task OnConfigureAsync(
        IPluginRegistrationContext registration,
        CancellationToken cancellationToken)
    {
        registration.AddCapability<IDataSourceReader>(
            _ => new PdfReader(registration.Context));

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<PluginHealth> CheckHealthAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(PdfReader.IsBackendAvailable
            ? PluginHealth.Healthy("Ready.")
            : PluginHealth.Unhealthy(
                "Format backend not wired.",
                "PdfPig or Pdfium is not yet bound in this build."));
}
