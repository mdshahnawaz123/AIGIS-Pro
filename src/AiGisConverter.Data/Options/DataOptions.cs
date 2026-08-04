using System.ComponentModel.DataAnnotations;

namespace AiGisConverter.Data.Options;

/// <summary>
/// Persistence configuration, bound from the <c>Database</c> section.
/// </summary>
public sealed class DataOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Database";

    /// <summary>Gets or sets the SQLite connection string. Environment variables are expanded.</summary>
    [Required]
    public string ConnectionString { get; set; } = "Data Source=%LOCALAPPDATA%\\AiGisConverter\\aigis.db";

    /// <summary>Gets or sets a value indicating whether pending migrations are applied on start-up.</summary>
    /// <remarks>
    /// Appropriate for a single-user desktop database. It would not be for a shared server, where
    /// two instances starting at once would race on the schema.
    /// </remarks>
    public bool AutoMigrate { get; set; } = true;

    /// <summary>Gets or sets the command timeout in seconds.</summary>
    [Range(1, 3600)]
    public int CommandTimeoutSeconds { get; set; } = 60;

    /// <summary>Gets or sets a value indicating whether EF Core logs parameter values.</summary>
    /// <remarks>
    /// Off by default. Parameter logging is invaluable when diagnosing a query and writes file
    /// paths and project names into the log, so it is a deliberate choice rather than a default.
    /// </remarks>
    public bool EnableSensitiveDataLogging { get; set; }

    /// <summary>Gets or sets how many days of run history are kept. Zero disables pruning.</summary>
    /// <remarks>
    /// Run history grows without bound on a machine doing nightly batches, and nothing else in the
    /// schema does. Pruning is the only reason this database ever needs maintenance.
    /// </remarks>
    [Range(0, 3650)]
    public int RunHistoryRetentionDays { get; set; } = 180;
}
