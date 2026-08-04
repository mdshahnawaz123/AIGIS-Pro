using Microsoft.Extensions.Logging;

namespace AiGisConverter.Infrastructure.FileSystem;

/// <summary>The real file system.</summary>
public sealed class PhysicalFileSystem : IFileSystem
{
    private readonly ILogger<PhysicalFileSystem> _logger;

    /// <summary>Initializes a new instance of the <see cref="PhysicalFileSystem"/> class.</summary>
    /// <param name="logger">Logger for access diagnostics.</param>
    public PhysicalFileSystem(ILogger<PhysicalFileSystem> logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        _logger = logger;
    }

    /// <inheritdoc />
    public bool FileExists(string path) => !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    /// <inheritdoc />
    public bool DirectoryExists(string path) => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path);

    /// <inheritdoc />
    public void CreateDirectory(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        Directory.CreateDirectory(path);
    }

    /// <inheritdoc />
    public IEnumerable<string> EnumerateFiles(string path, string searchPattern, bool recursive = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(searchPattern);

        if (!Directory.Exists(path))
        {
            return [];
        }

        return Directory.EnumerateFiles(
            path,
            searchPattern,
            recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
    }

    /// <inheritdoc />
    public long GetFileSize(string path)
    {
        if (!FileExists(path))
        {
            return 0L;
        }

        try
        {
            return new FileInfo(path).Length;
        }
        catch (IOException)
        {
            return 0L;
        }
    }

    /// <inheritdoc />
    public bool CanWriteTo(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        string probe = Path.Combine(path, $".aigis-write-probe-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(path);

            using (File.Create(probe, 1, FileOptions.DeleteOnClose))
            {
                return true;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogDebug(ex, "The directory {Path} is not writable.", path);
            return false;
        }
    }

    /// <inheritdoc />
    public string ResolvePath(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        string expanded = Environment.ExpandEnvironmentVariables(path);

        return Path.IsPathRooted(expanded) ? expanded : Path.Combine(AppContext.BaseDirectory, expanded);
    }

    /// <inheritdoc />
    public string GetAvailablePath(string desiredPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(desiredPath);

        if (!File.Exists(desiredPath))
        {
            return desiredPath;
        }

        string directory = Path.GetDirectoryName(desiredPath) ?? string.Empty;
        string stem = Path.GetFileNameWithoutExtension(desiredPath);
        string extension = Path.GetExtension(desiredPath);

        // Bounded rather than a while(true). A directory that somehow holds ten thousand
        // conversions of the same drawing is a different problem, and spinning is not the answer.
        for (int counter = 1; counter <= 10_000; counter++)
        {
            string candidate = Path.Combine(directory, $"{stem} ({counter}){extension}");

            if (!File.Exists(candidate))
            {
                return candidate;
            }
        }

        return Path.Combine(directory, $"{stem} ({Guid.NewGuid():N}){extension}");
    }
}
