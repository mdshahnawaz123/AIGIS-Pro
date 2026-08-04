using System.Text.RegularExpressions;

namespace AiGisConverter.Cad.Crs;

/// <summary>
/// Reads the coordinate reference system from a <c>.prj</c> file sitting beside a drawing.
/// </summary>
/// <remarks>
/// <para>
/// This is the most reliable CRS signal available for a CAD file, because it was written
/// deliberately by whoever exported the data, rather than inferred. It is checked before any
/// heuristic.
/// </para>
/// <para>
/// The parser extracts only the authority code. Interpreting the full WKT is the GIS layer's work;
/// the CAD layer's job is to notice the file exists and carry its contents forward.
/// </para>
/// </remarks>
public static partial class SidecarCrsReader
{
    /// <summary>The sidecar extensions checked, in priority order.</summary>
    private static readonly string[] SidecarExtensions = [".prj", ".prj.txt"];

    /// <summary>Attempts to read a CRS declaration for a drawing.</summary>
    /// <param name="drawingPath">The path of the drawing file.</param>
    /// <param name="result">The declaration, when a sidecar was found and understood.</param>
    /// <returns><see langword="true"/> when a declaration was found.</returns>
    public static bool TryRead(string drawingPath, out SidecarCrs? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(drawingPath))
        {
            return false;
        }

        string? directory = Path.GetDirectoryName(drawingPath);
        string stem = Path.GetFileNameWithoutExtension(drawingPath);

        if (directory is null || string.IsNullOrEmpty(stem))
        {
            return false;
        }

        foreach (string extension in SidecarExtensions)
        {
            string candidate = Path.Combine(directory, stem + extension);

            if (!File.Exists(candidate))
            {
                continue;
            }

            string wkt;

            try
            {
                wkt = File.ReadAllText(candidate).Trim();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            if (wkt.Length == 0)
            {
                continue;
            }

            result = new SidecarCrs(candidate, wkt, ExtractAuthorityCode(wkt));
            return true;
        }

        return false;
    }

    /// <summary>
    /// Pulls an authority identifier out of a WKT definition.
    /// </summary>
    /// <remarks>
    /// The last <c>AUTHORITY</c> clause in a WKT string is the one describing the outermost
    /// coordinate system; earlier ones describe the datum, the ellipsoid and the units. Taking the
    /// first match is a common and quietly wrong shortcut that reports a drawing as being in the
    /// Greenwich prime meridian's code.
    /// </remarks>
    /// <param name="wkt">The WKT definition.</param>
    /// <returns>The identifier such as <c>EPSG:27700</c>, or null when none is present.</returns>
    public static string? ExtractAuthorityCode(string wkt)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return null;
        }

        MatchCollection matches = AuthorityPattern().Matches(wkt);

        if (matches.Count == 0)
        {
            return null;
        }

        Match last = matches[^1];

        return $"{last.Groups["authority"].Value.ToUpperInvariant()}:{last.Groups["code"].Value}";
    }

    [GeneratedRegex(
        """(?:AUTHORITY|ID)\s*\[\s*"(?<authority>[A-Za-z]+)"\s*,\s*"?(?<code>\d+)"?\s*\]""",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AuthorityPattern();
}

/// <summary>A coordinate reference system declaration found beside a drawing.</summary>
/// <param name="SidecarPath">The file it was read from.</param>
/// <param name="WellKnownText">The full definition.</param>
/// <param name="AuthorityCode">The extracted identifier such as <c>EPSG:27700</c>, when present.</param>
public sealed record SidecarCrs(string SidecarPath, string WellKnownText, string? AuthorityCode);
