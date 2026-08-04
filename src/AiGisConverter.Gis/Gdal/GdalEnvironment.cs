// ---------------------------------------------------------------------------------------------
// GDAL BOUNDARY FILE (1 of 4). See Crs/GdalCrsRegistry.cs, Crs/GdalCoordinateTransformer.cs and
// Exporters/Ogr/OgrExporterBase.cs.
//
// Every reference to OSGeo.* in this assembly lives in those four files. Nothing else in the GIS
// layer knows GDAL exists, so a native-binding problem is contained to a boundary that can be
// swapped without touching geometry, validation, profiles or the streaming exporters.
// ---------------------------------------------------------------------------------------------

using AiGisConverter.Gis.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Gis.Gdal;

/// <summary>
/// One-time initialisation of the native GDAL, OGR and PROJ libraries.
/// </summary>
/// <remarks>
/// <para>
/// GDAL's driver registration is process-global and not re-entrant. Calling it twice is
/// undefined; calling it from two threads at once is worse. This gate makes initialisation happen
/// exactly once, whatever order the container resolves things in.
/// </para>
/// <para>
/// Initialisation failure is recorded rather than thrown. A workstation with a broken native
/// deployment should still be able to open the application and export GeoJSON, which needs no
/// GDAL at all.
/// </para>
/// </remarks>
public sealed class GdalEnvironment
{
    private readonly Lazy<InitialisationResult> _initialisation;
    private readonly ILogger<GdalEnvironment> _logger;

    /// <summary>Initializes a new instance of the <see cref="GdalEnvironment"/> class.</summary>
    /// <param name="options">GIS settings supplying the data paths.</param>
    /// <param name="logger">Logger for initialisation diagnostics.</param>
    public GdalEnvironment(IOptions<GisOptions> options, ILogger<GdalEnvironment> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _logger = logger;
        _initialisation = new Lazy<InitialisationResult>(
            () => Initialise(options.Value.Crs),
            LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>Gets a value indicating whether the native libraries loaded.</summary>
    public bool IsAvailable => _initialisation.Value.Succeeded;

    /// <summary>Gets the reason initialisation failed, when it did.</summary>
    public string? FailureReason => _initialisation.Value.Reason;

    /// <summary>Ensures the native libraries are ready.</summary>
    /// <returns><see langword="true"/> when GDAL can be used.</returns>
    public bool Ensure() => _initialisation.Value.Succeeded;

    private InitialisationResult Initialise(CrsOptions options)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(options.GdalDataPath))
            {
                Environment.SetEnvironmentVariable("GDAL_DATA", options.GdalDataPath);
            }

            if (!string.IsNullOrWhiteSpace(options.ProjDataPath))
            {
                Environment.SetEnvironmentVariable("PROJ_LIB", options.ProjDataPath);
            }

            // MaxRev.Gdal.Core resolves the native payload for the running platform and calls
            // GdalAllRegister and OGRRegisterAll. Doing it by hand risks a partial registration.
            MaxRev.Gdal.Core.GdalBase.ConfigureAll();

            _logger.LogInformation(
                "GDAL initialised. Version {Version}.",
                OSGeo.GDAL.Gdal.VersionInfo("RELEASE_NAME"));

            return new InitialisationResult(true, null);
        }
        catch (Exception ex) when (ex is DllNotFoundException
                                       or BadImageFormatException
                                       or TypeInitializationException
                                       or EntryPointNotFoundException
                                       or DirectoryNotFoundException
                                       or FileNotFoundException)
        {
            // A type-initializer failure says only that some static constructor threw; the reason
            // ("Unable to load DLL 'gdal_wrap'", a missing data directory) is one or more levels
            // down. Without unwrapping it the operator is told nothing they can act on.
            string reason =
                $"The native GDAL libraries could not be loaded: {Describe(ex)} " +
                "Shapefile, GeoPackage and coordinate transformation are unavailable; " +
                "GeoJSON, KML, CSV, WKT and WKB are not affected.";

            _logger.LogError(ex, "GDAL initialisation failed. {Reason}", reason);

            return new InitialisationResult(false, reason);
        }
    }

    /// <summary>Flattens an exception chain into one diagnosable sentence.</summary>
    /// <param name="exception">The exception to describe.</param>
    /// <returns>The message of the exception and every inner exception.</returns>
    private static string Describe(Exception exception)
    {
        List<string> messages = [];

        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            messages.Add($"{current.GetType().Name}: {current.Message}");
        }

        return string.Join(" -> ", messages);
    }

    private sealed record InitialisationResult(bool Succeeded, string? Reason);
}
