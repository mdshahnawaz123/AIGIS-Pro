using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Cad.Providers.AutoCad;

/// <summary>
/// The DWG engine used when no licensed engine is present. Reports its own absence.
/// </summary>
/// <remarks>
/// It fails with an actionable sentence rather than a missing-assembly exception. "Install the
/// AI GIS Converter add-in for AutoCAD, or save the drawing as DXF" is something a user can act on;
/// a <see cref="FileNotFoundException"/> naming <c>acdbmgd</c> is not.
/// </remarks>
public sealed class UnavailableDwgBackend : IDwgBackend
{
    /// <summary>The failure message shown when a DWG is opened with no engine available.</summary>
    public const string Explanation =
        "DWG support is not enabled in this build. Either save the drawing as DXF, or install the " +
        "AI GIS Converter add-in for AutoCAD and use the AutoCAD reader plugin.";

    /// <inheritdoc />
    public string EngineDescription => "none";

    /// <inheritdoc />
    public Task<CadProviderAvailability> ProbeAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CadProviderAvailability.Unavailable(Explanation));

    /// <inheritdoc />
    public Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress,
        CancellationToken cancellationToken) =>
        Task.FromResult(Result.Failure<SourceDocument>(new Error("Cad.DwgEngineUnavailable", Explanation)));
}
