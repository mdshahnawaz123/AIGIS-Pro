using AiGisConverter.Cad.Abstractions;
using AiGisConverter.Domain.Abstractions.Services;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Cad.Factories;

/// <summary>
/// Presents one <see cref="ICadProvider"/> to the application as a domain
/// <see cref="IDataSourceReader"/>.
/// </summary>
/// <remarks>
/// A thin adapter, deliberately one instance per provider rather than one covering all of them.
/// The file-open dialog then lists "AutoCAD DXF" and "AutoCAD DWG" separately, which is what a
/// user expects, and the reader catalogue can report each format's availability independently.
/// </remarks>
public sealed class CadDataSourceReader : IDataSourceReader
{
    private readonly ICadProvider _provider;

    /// <summary>Initializes a new instance of the <see cref="CadDataSourceReader"/> class.</summary>
    /// <param name="provider">The provider to expose.</param>
    public CadDataSourceReader(ICadProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        _provider = provider;
    }

    /// <inheritdoc />
    public string FormatKey => _provider.Key;

    /// <inheritdoc />
    public string DisplayName => _provider.DisplayName;

    /// <inheritdoc />
    public IReadOnlyList<string> SupportedExtensions => _provider.SupportedExtensions;

    /// <inheritdoc />
    public bool CanRead(SourceReference reference) => _provider.CanRead(reference);

    /// <inheritdoc />
    public Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default) =>
        _provider.ReadAsync(reference, progress, cancellationToken);
}
