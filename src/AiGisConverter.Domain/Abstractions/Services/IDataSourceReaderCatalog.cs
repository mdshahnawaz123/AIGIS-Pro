using AiGisConverter.Domain.Entities.Source;

namespace AiGisConverter.Domain.Abstractions.Services;

/// <summary>
/// The set of readers currently available, however they arrived.
/// </summary>
/// <remarks>
/// The application layer asks this port which reader can open a file. It cannot know that some
/// readers were compiled in and others were contributed by a plugin discovered at start-up, and
/// nothing about its behaviour should change when that balance shifts.
/// </remarks>
public interface IDataSourceReaderCatalog
{
    /// <summary>Gets every available reader.</summary>
    /// <returns>The readers, in a deterministic order.</returns>
    IReadOnlyList<IDataSourceReader> GetReaders();

    /// <summary>Finds the reader that claims a source.</summary>
    /// <param name="reference">The source to open.</param>
    /// <returns>The first reader claiming the source, or <see langword="null"/> when none does.</returns>
    IDataSourceReader? FindReader(SourceReference reference);

    /// <summary>Gets every file extension any reader accepts, for the file-open dialog filter.</summary>
    /// <returns>The distinct extensions, each including the leading dot.</returns>
    IReadOnlyList<string> GetSupportedExtensions();
}
