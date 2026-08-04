using AiGisConverter.Data.Converters;
using AiGisConverter.Domain.Entities.Project;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiGisConverter.Data.Configurations;

/// <summary>Maps <see cref="ConversionRun"/>.</summary>
/// <remarks>
/// Status, timestamps and severity are real columns because that is what run history is queried
/// by: what failed last night, what needs attention, what is older than the retention window.
/// Settings and the resolved coordinate system are JSON because they are read as a unit and never
/// filtered on.
/// </remarks>
public sealed class ConversionRunConfiguration : IEntityTypeConfiguration<ConversionRun>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConversionRun> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Runs");
        builder.HasKey(run => run.Id);

        builder.Property(run => run.Id)
            .HasConversion(DomainValueConverters.RunId)
            .ValueGeneratedNever();

        builder.Property(run => run.JobId).HasConversion(DomainValueConverters.JobId).IsRequired();
        builder.Property(run => run.ProjectId).HasConversion(DomainValueConverters.ProjectId).IsRequired();

        builder.Property(run => run.Settings)
            .HasConversion(DomainValueConverters.Settings)
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(run => run.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.CrsSource).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(run => run.HighestSeverity).HasConversion<string>().HasMaxLength(16).IsRequired();

        builder.Property(run => run.ResolvedCoordinateSystem)
            .HasConversion(DomainValueConverters.CoordinateSystem)
            .HasColumnType("TEXT");

        builder.Property(run => run.StartedAtUtc);
        builder.Property(run => run.FinishedAtUtc);
        builder.Property(run => run.ElementsRead);
        builder.Property(run => run.FeaturesWritten);
        builder.Property(run => run.IssueCount);
        builder.Property(run => run.FailureReason).HasMaxLength(4_000);

        // The output paths are a list the aggregate appends to through RecordOutput; there is no
        // setter, so the backing field is the only honest way in. Mapping the field rather than
        // adding a shadow property keeps one column in step with the aggregate instead of two.
        builder.Property<List<string>>("_outputPaths")
            .HasColumnName("OutputPaths")
            .HasConversion(DomainValueConverters.StringList)
            .HasColumnType("TEXT");

        builder.Ignore(run => run.OutputPaths);
        builder.Ignore(run => run.Duration);
        builder.Ignore(run => run.DomainEvents);

        builder.HasIndex(run => run.ProjectId);
        builder.HasIndex(run => run.JobId);
        builder.HasIndex(run => run.FinishedAtUtc);
        builder.HasIndex(run => run.Status);
    }
}
