using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// Driven port for reading a source into the format-neutral <see cref="SourceDocument"/>.
/// </summary>
/// <remarks>
/// This is the contract every input plugin implements &#8212; AutoCAD, Civil 3D, Revit, IFC, DGN,
/// PDF, point cloud, LiDAR and drone. The application layer never learns which one answered.
/// </remarks>
public interface IDataSourceReader
{
    /// <summary>Gets the reader's format key, for example <c>dwg</c>, <c>ifc</c> or <c>las</c>.</summary>
    string FormatKey { get; }

    /// <summary>Gets the human-readable format name shown in the file-open dialog.</summary>
    string DisplayName { get; }

    /// <summary>Gets the file extensions this reader handles, each including the leading dot.</summary>
    IReadOnlyList<string> SupportedExtensions { get; }

    /// <summary>Determines whether this reader can handle the reference.</summary>
    /// <param name="reference">The source to test.</param>
    /// <returns><see langword="true"/> when this reader claims the source.</returns>
    bool CanRead(SourceReference reference);

    /// <summary>Reads the source.</summary>
    /// <param name="reference">The source to read.</param>
    /// <param name="progress">Optional progress sink, reported as a fraction with a status message.</param>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The document, or a failure describing why the source could not be read.</returns>
    Task<Result<SourceDocument>> ReadAsync(
        SourceReference reference,
        IProgress<ReadProgress>? progress = null,
        CancellationToken cancellationToken = default);
}

/// <summary>Progress reported while reading a source.</summary>
/// <param name="Fraction">Completion in the closed interval <c>[0, 1]</c>, or null when indeterminate.</param>
/// <param name="Message">Short status message suitable for a status bar.</param>
public readonly record struct ReadProgress(double? Fraction, string Message);
