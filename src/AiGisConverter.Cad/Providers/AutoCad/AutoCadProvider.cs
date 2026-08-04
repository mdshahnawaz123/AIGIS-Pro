using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using Microsoft.Extensions.Logging;

namespace AiGisConverter.Cad.Providers.AutoCad;

/// <summary>
/// The DWG provider. Compiles and runs with no Autodesk SDK installed.
/// </summary>
/// <remarks>
/// <para>
/// This type contains no Autodesk reference of any kind. It claims <c>.dwg</c> so that opening one
/// produces a clear explanation rather than "no reader claimed this file", and delegates the actual
/// work to whichever <see cref="IDwgBackend"/> is registered.
/// </para>
/// <para>
/// Claiming a format it may not be able to read is deliberate. The alternative &#8212; staying
/// silent &#8212; leaves the user staring at a file dialog that will not accept their drawing, with
/// nothing telling them why.
/// </para>
/// </remarks>
public sealed class AutoCadProvider : ICadProvider
{
    /// <summary>The provider key.</summary>
    public const string ProviderKey = "dwg";

    private readonly IDwgBackend _backend;
    private readonly ILogger<AutoCadProvider> _logger;

    /// <summary>Initializes a new instance of the <see cref="AutoCadProvider"/> class.</summary>
    /// <param name="backend">The DWG engine, or <see cref="UnavailableDwgBackend"/> when there is none.</param>
    /// <param name="logger">Logger for the provider.</param>
    public AutoCadProvider(IDwgBackend backend, ILogger<AutoCadProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(backend);
        ArgumentNullException.ThrowIfNull(logger);

        _backend = backend;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Key => ProviderKey;

    /// <inheritdoc />
    public string DisplayName => "AutoCAD DWG";

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions { get; } = [".dwg"];

    /// <inheritdoc />
    public bool CanRead(SourceReference reference)
    {
        ArgumentNullException.ThrowIfNull(reference);

        return SupportedExtensions.Contains(reference.Extension, StringComparer.OrdinalIgnoreCase)
               && File.Exists(reference.Location);
    }

    /// <inheritdoc />
    public Task<CadProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default) =>
        _backend.ProbeAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);

        CadProviderAvailability availability = await _backend.ProbeAsync(cancellationToken).ConfigureAwait(false);

        if (!availability.IsAvailable)
        {
            _logger.LogWarning(
                "A DWG was opened but no DWG engine is available: {Reason}",
                availability.Reason);

            return Result.Failure<SourceDocument>(new Error(
                "Cad.DwgEngineUnavailable",
                availability.Reason ?? UnavailableDwgBackend.Explanation));
        }

        return await _backend.ReadAsync(reference, progress, cancellationToken).ConfigureAwait(false);
    }
}
