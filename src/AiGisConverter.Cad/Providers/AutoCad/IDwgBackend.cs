using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Cad.Providers.AutoCad;

/// <summary>
/// The engine that actually opens a DWG.
/// </summary>
/// <remarks>
/// <para>
/// DWG is a closed format: reading it needs either the Autodesk .NET API running inside AutoCAD,
/// a RealDWG licence, or the Open Design Alliance SDK. None of those can be a hard dependency of a
/// build that must succeed on a machine with none of them installed.
/// </para>
/// <para>
/// So <see cref="AutoCadProvider"/> depends on this abstraction and nothing else. The default
/// implementation reports the engine as absent; a licensed build supplies a real one from
/// <c>Providers/AutoCad/Interop</c>, which is excluded from compilation unless
/// <c>EnableAutoCadProvider</c> is set. No Autodesk type appears anywhere in the default build.
/// </para>
/// </remarks>
public interface IDwgBackend
{
    /// <summary>Gets a description of the engine, for diagnostics.</summary>
    string EngineDescription { get; }

    /// <summary>Checks whether the engine can be used on this machine right now.</summary>
    /// <param name="cancellationToken">Token used to cancel the probe.</param>
    /// <returns>The availability. Implementations must report, not throw.</returns>
    Task<CadProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads a DWG.</summary>
    /// <param name="reference">The drawing to read.</param>
    /// <param name="progress">Optional progress sink.</param>
    /// <param name="cancellationToken">Token used to cancel the read.</param>
    /// <returns>The document, or a failure describing why it could not be read.</returns>
    Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress,
        CancellationToken cancellationToken);
}
