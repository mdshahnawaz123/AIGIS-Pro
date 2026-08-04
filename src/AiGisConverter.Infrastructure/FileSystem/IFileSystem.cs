namespace AiGisConverter.Infrastructure.FileSystem;

/// <summary>
/// File operations, as a dependency.
/// </summary>
/// <remarks>
/// Narrow on purpose. It covers what the conversion pipeline actually does &#8212; check a path,
/// prepare an output folder, find where the application may write &#8212; rather than mirroring
/// <see cref="System.IO.File"/>. A wide abstraction over the file system is a second file system
/// with its own bugs.
/// </remarks>
public interface IFileSystem
{
    /// <summary>Determines whether a file exists.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the file is present.</returns>
    bool FileExists(string path);

    /// <summary>Determines whether a directory exists.</summary>
    /// <param name="path">The path to test.</param>
    /// <returns><see langword="true"/> when the directory is present.</returns>
    bool DirectoryExists(string path);

    /// <summary>Creates a directory and every missing parent.</summary>
    /// <param name="path">The directory to create.</param>
    void CreateDirectory(string path);

    /// <summary>Lists files matching a pattern.</summary>
    /// <param name="path">The directory to search.</param>
    /// <param name="searchPattern">The pattern, for example <c>*.dxf</c>.</param>
    /// <param name="recursive">Whether sub-directories are included.</param>
    /// <returns>The matching paths.</returns>
    IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false);

    /// <summary>Gets the size of a file in bytes.</summary>
    /// <param name="path">The file to measure.</param>
    /// <returns>The size, or zero when the file is absent.</returns>
    long GetFileSize(string path);

    /// <summary>
    /// Checks whether a directory can actually be written to.
    /// </summary>
    /// <remarks>
    /// Tested by writing, not by inspecting permissions. On Windows the effective right depends on
    /// the token, the share, the ACL and any redirection in force, and the only reliable answer is
    /// to try.
    /// </remarks>
    /// <param name="path">The directory to test.</param>
    /// <returns><see langword="true"/> when a file could be created there.</returns>
    bool CanWriteTo(string path);

    /// <summary>Expands environment variables and resolves a relative path against the application folder.</summary>
    /// <param name="path">The path to resolve.</param>
    /// <returns>The absolute path.</returns>
    string ResolvePath(string path);

    /// <summary>Produces a path that does not yet exist, by appending a counter if needed.</summary>
    /// <param name="desiredPath">The preferred path.</param>
    /// <returns>A path no file currently occupies.</returns>
    string GetAvailablePath(string desiredPath);
}
