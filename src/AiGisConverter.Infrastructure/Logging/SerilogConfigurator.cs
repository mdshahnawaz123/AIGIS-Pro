using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace AiGisConverter.Infrastructure.Logging;

/// <summary>
/// Builds the application logger.
/// </summary>
/// <remarks>
/// <para>
/// Configured from <c>appsettings.json</c> where possible, with a working default when that
/// section is absent or malformed. A logging misconfiguration must not be the thing that stops the
/// application starting, because then nothing records why.
/// </para>
/// <para>
/// Paths in the configuration carry environment variables, which Serilog does not expand itself.
/// </para>
/// </remarks>
public static class SerilogConfigurator
{
    /// <summary>Creates the logger.</summary>
    /// <param name="configuration">Application configuration containing the <c>Serilog</c> section.</param>
    /// <returns>The configured logger.</returns>
    public static ILogger Create(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        try
        {
            if (configuration.GetSection("Serilog").Exists())
            {
                return new LoggerConfiguration()
                    .ReadFrom.Configuration(configuration)
                    .Enrich.FromLogContext()
                    .CreateLogger();
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException)
        {
            // Fall through to the default. Reported below, once there is a logger to report with.
            return CreateFallback(ex);
        }

        return CreateFallback(null);
    }

    /// <summary>Creates a logger that works with no configuration at all.</summary>
    private static ILogger CreateFallback(Exception? configurationFailure)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "AiGisConverter",
            "logs",
            "aigis-.log");

        ILogger logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.Debug()
            .WriteTo.File(
                path,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 30,
                outputTemplate:
                    "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
            .CreateLogger();

        if (configurationFailure is not null)
        {
            logger.Warning(
                configurationFailure,
                "The Serilog configuration could not be read; default logging to {Path} is in use.",
                path);
        }

        return logger;
    }
}
