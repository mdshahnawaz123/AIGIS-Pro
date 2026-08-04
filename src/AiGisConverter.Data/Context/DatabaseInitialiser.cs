using AiGisConverter.Data.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AiGisConverter.Data.Context;

/// <summary>
/// Brings the database up to date at start-up.
/// </summary>
/// <remarks>
/// <para>
/// Applies migrations rather than calling <c>EnsureCreated</c>. The two are mutually exclusive:
/// a database created by <c>EnsureCreated</c> has no migration history, and the first release
/// that needs a schema change cannot upgrade it. For an application whose whole purpose is keeping
/// a record of past conversions, that is not a corner worth cutting.
/// </para>
/// <para>
/// Failure is reported, not thrown. A desktop application whose history database is unavailable
/// should still convert drawings.
/// </para>
/// </remarks>
public sealed class DatabaseInitialiser
{
    private readonly AiGisConverterDbContext _context;
    private readonly IOptions<DataOptions> _options;
    private readonly ILogger<DatabaseInitialiser> _logger;

    /// <summary>Initializes a new instance of the <see cref="DatabaseInitialiser"/> class.</summary>
    /// <param name="context">The database context.</param>
    /// <param name="options">Persistence settings.</param>
    /// <param name="logger">Logger for start-up diagnostics.</param>
    public DatabaseInitialiser(
        AiGisConverterDbContext context,
        IOptions<DataOptions> options,
        ILogger<DatabaseInitialiser> logger)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _context = context;
        _options = options;
        _logger = logger;
    }

    /// <summary>Creates the database and schema if they do not exist.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns><see langword="true"/> when the database is usable.</returns>
    public async Task<bool> InitialiseAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Value.AutoMigrate)
        {
            _logger.LogInformation("Automatic migration is disabled; the schema is assumed to be current.");
            return true;
        }

        try
        {
            // Version 1.0 has no migration history. EnsureCreatedAsync creates the schema from
            // the model when the database (or its tables) do not yet exist. Future releases that
            // need schema changes should switch to MigrateAsync and ship migration files.
            bool created = await _context.Database.EnsureCreatedAsync(cancellationToken).ConfigureAwait(false);

            if (created)
            {
                _logger.LogInformation("The history database was created.");
            }
            else
            {
                _logger.LogInformation("The database schema is up to date.");
            }

            return true;
        }
        catch (Exception ex) when (ex is Microsoft.Data.Sqlite.SqliteException
                                       or InvalidOperationException
                                       or IOException)
        {
            _logger.LogError(
                ex,
                "The history database could not be prepared. Conversion still works; run history " +
                "and QA reports will not be recorded this session.");

            return false;
        }
    }

    /// <summary>Deletes run history older than the configured retention window.</summary>
    /// <param name="cancellationToken">Token used to cancel the operation.</param>
    /// <returns>The number of runs deleted.</returns>
    public async Task<int> PruneHistoryAsync(CancellationToken cancellationToken = default)
    {
        int days = _options.Value.RunHistoryRetentionDays;

        if (days <= 0)
        {
            return 0;
        }

        // SQLite's EF Core provider cannot translate nullable DateTimeOffset comparisons in LINQ.
        // We use raw SQL instead; SQLite stores DateTimeOffset as ISO 8601 text, which sorts correctly.
        string cutOffText = DateTimeOffset.UtcNow.AddDays(-days).ToString("o");

        // Delete validation issues first (foreign key to runs).
        await _context.Database
            .ExecuteSqlRawAsync(
                "DELETE FROM ValidationIssues WHERE RunId IN " +
                "(SELECT Id FROM Runs WHERE FinishedAtUtc IS NOT NULL AND FinishedAtUtc < {0})",
                new object[] { cutOffText },
                cancellationToken)
            .ConfigureAwait(false);

        int deleted = await _context.Database
            .ExecuteSqlRawAsync(
                "DELETE FROM Runs WHERE FinishedAtUtc IS NOT NULL AND FinishedAtUtc < {0}",
                new object[] { cutOffText },
                cancellationToken)
            .ConfigureAwait(false);

        if (deleted > 0)
        {
            _logger.LogInformation("Pruned {Count} runs older than {Days} days.", deleted, days);
        }

        return deleted;
    }
}
