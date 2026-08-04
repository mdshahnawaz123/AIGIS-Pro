using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Cad.Abstractions;

/// <summary>
/// One CAD format engine.
/// </summary>
/// <remarks>
/// <para>
/// Distinct from the domain's <see cref="IDataSourceReader"/> in one respect that matters:
/// a provider can report that it is <em>installed but unusable</em> through
/// <see cref="ProbeAsync"/>. DWG needs a licensed engine that may be absent; DXF never is. The
/// domain port has no vocabulary for that distinction, and inventing one there would push a CAD
/// concern into the domain.
/// </para>
/// <para>
/// Every provider returns only domain models. Nothing vendor-shaped crosses this boundary, which
/// is what lets the same pipeline consume DXF, DWG and, later, anything else.
/// </para>
/// </remarks>
public interface ICadProvider
{
    /// <summary>Gets the provider key, for example <c>dxf</c> or <c>dwg</c>.</summary>
    string Key { get; }

    /// <summary>Gets the human-readable name shown in the file-open dialog.</summary>
    string DisplayName { get; }

    /// <summary>Gets the file extensions this provider handles, each including the leading dot.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Determines whether this provider claims a source.</summary>
    /// <param name="reference">The source to test.</param>
    /// <returns><see langword="true"/> when the provider handles this file.</returns>
    bool CanRead(SourceReference reference);

    /// <summary>Checks whether the provider's engine is present and usable.</summary>
    /// <param name="cancellationToken">Token used to cancel the probe.</param>
    /// <returns>The availability. Implementations must report, not throw.</returns>
    Task<CadProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a drawing into the domain's format-neutral source model.</summary>
    /// <param name="reference">The drawing to read.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>The document, or a failure describing why it could not be read.</returns>
    Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Whether a CAD provider can currently do any work.</summary>
/// <param name="IsAvailable">Whether the engine is usable.</param>
/// <param name="Reason">Why it is not, when it is not.</param>
/// <param name="EngineDescription">What engine answered, for diagnostics.</param>
public sealed record CadProviderAvailability(bool IsAvailable, string? Reason, string? EngineDescription)
{
    /// <summary>Creates an available result.</summary>
    /// <param name="engineDescription">The engine in use.</param>
    /// <returns>An available <see cref="CadProviderAvailability"/>.</returns>
    public static CadProviderAvailability Available(string engineDescription) =>
        new(true, null, engineDescription);

    /// <summary>Creates an unavailable result.</summary>
    /// <param name="reason">Why the engine cannot be used.</param>
    /// <returns>An unavailable <see cref="CadProviderAvailability"/>.</returns>
    public static CadProviderAvailability Unavailable(string reason) => new(false, reason, null);
}
