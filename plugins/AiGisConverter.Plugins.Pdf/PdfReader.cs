using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Plugins.Abstractions;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Plugins.Pdf;

/// <summary>
/// PDF Vector Reader.
/// </summary>
/// <remarks>
/// <para>
/// Format detection and the plugin contract are complete; the backend binding is not.
/// Implement <see cref="ReadAsync"/> against PdfPig or Pdfium.
/// </para>
/// <para>
/// Extract vector paths and text runs; georeference from an OGC GeoPDF viewport when present.
/// </para>
/// <para>
/// The reader returns a failed <see cref="Result"/> rather than throwing, so an unbound backend
/// shows the user one clear sentence instead of aborting a batch run.
/// </para>
/// </remarks>
internal sealed class PdfReader : IDataSourceReader
{
    private readonly IPluginContext _context;

    /// <summary>Initializes a new instance of the <see cref="PdfReader"/> class.</summary>
    /// <param name="context">The plugin context.</param>
    public PdfReader(IPluginContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        _context = context;
    }

    /// <summary>
    /// Gets a value indicating whether the format backend is bound in this build.
    /// Flip to true in the same change that implements <see cref="ReadAsync"/>.
    /// </summary>
    public static bool IsBackendAvailable => false;

    /// <inheritdoc />
    public string FormatKey => "pdf";

    /// <inheritdoc />
    public string DisplayName => "PDF Vector Reader";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = [".pdf"];

    /// <inheritdoc />
    public bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase)
               && File.Exists(reference.Location);
    }

    /// <inheritdoc />
    public Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        _context.Logger.LogWarning(
            "{Reader} recognised '{Location}' but its format backend is not bound in this build.",
            DisplayName,
            reference.Location);

        return Task.FromResult(Result.Failure<SourceDocument>(new Error(
            "Plugin.BackendNotBound",
            "The PDF Vector Reader recognises this file but its format backend is not bound in this " +
            "build. Bind PdfPig or Pdfium in PdfReader.ReadAsync.")));
    }
}
