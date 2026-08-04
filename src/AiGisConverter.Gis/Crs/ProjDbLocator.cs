using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Crs;

/// <summary>
/// Finds PROJ's <c>proj.db</c> on disk so the catalogue can query it directly.
/// </summary>
/// <remarks>
/// The file ships inside the MaxRev.Gdal native payload and is copied beside the application, but
/// the exact sub-path varies by runtime identifier. Rather than hard-code one location, the
/// resolver checks the configured PROJ path, the environment PROJ makes available, and then the
/// output tree, caching the first hit.
/// </remarks>
public sealed class ProjDbLocator
{
    private readonly IOptions<GisOptions> _options;
    private readonly Lazy<string?> _path;

    /// <summary>Initializes a new instance of the <see cref="ProjDbLocator"/> class.</summary>
    /// <param name="options">GIS options, which may carry an explicit PROJ data path.</param>
    public ProjDbLocator(IOptions<GisOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options;
        _path = new Lazy<string?>(Locate);
    }

    /// <summary>Gets the full path to <c>proj.db</c>, or null when it could not be found.</summary>
    public string? Path => _path.Value;

    private string? Locate()
    {
        foreach (string candidate in CandidateDirectories())
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            string direct = System.IO.Path.Combine(candidate, "proj.db");

            if (File.Exists(direct))
            {
                return direct;
            }
        }

        // Last resort: a bounded search of the output tree for the native payload's copy.
        try
        {
            string root = AppContext.BaseDirectory;

            foreach (string found in Directory.EnumerateFiles(root, "proj.db", SearchOption.AllDirectories))
            {
                return found;
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }

        return null;
    }

    private IEnumerable<string> CandidateDirectories()
    {
        yield return _options.Value.Crs.ProjDataPath;
        yield return Environment.GetEnvironmentVariable("PROJ_LIB") ?? string.Empty;
        yield return Environment.GetEnvironmentVariable("PROJ_DATA") ?? string.Empty;
        yield return AppContext.BaseDirectory;
        yield return System.IO.Path.Combine(
            AppContext.BaseDirectory, "runtimes", "win-x64", "native", "maxrev.gdal.core.libshared");
    }
}
