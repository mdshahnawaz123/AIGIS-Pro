using AiGisConverter.Domain.Entities.Project;
using AiGisConverter.Domain.Entities.QaQc;
using Microsoft.EntityFrameworkCore;

namespace AiGisConverter.Data.Context;

/// <summary>
/// The application's SQLite database.
/// </summary>
/// <remarks>
/// <para>
/// Holds two aggregates and the reports belonging to them. Nothing spatial is stored: converted
/// features are written to GIS files and are not the database's business. What is stored is the
/// record of what was converted, under which settings, and what the QA found &#8212; the things
/// somebody asks about six months later.
/// </para>
/// <para>
/// Configuration lives in <c>IEntityTypeConfiguration</c> classes rather than in
/// <see cref="OnModelCreating"/>, so a mapping question is answered by opening one file named
/// after the entity.
/// </para>
/// </remarks>
public sealed class AiGisConverterDbContext : DbContext
{
    /// <summary>Initializes a new instance of the <see cref="AiGisConverterDbContext"/> class.</summary>
    /// <param name="options">The context options.</param>
    public AiGisConverterDbContext(DbContextOptions<AiGisConverterDbContext> options)
        : base(options)
    {
    }

    /// <summary>Gets the conversion projects.</summary>
    public DbSet<ConversionProject> Projects => Set<ConversionProject>();

    /// <summary>Gets the conversion jobs. Reached through their project; exposed for querying.</summary>
    public DbSet<ConversionJob> Jobs => Set<ConversionJob>();

    /// <summary>Gets the conversion runs.</summary>
    public DbSet<ConversionRun> Runs => Set<ConversionRun>();

    /// <summary>Gets the stored validation findings.</summary>
    public DbSet<ValidationIssue> ValidationIssues => Set<ValidationIssue>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AiGisConverterDbContext).Assembly);

        base.OnModelCreating(modelBuilder);
    }
}
