using AiGisConverter.Data.Converters;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Project;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiGisConverter.Data.Configurations;

/// <summary>Maps <see cref="ConversionProject"/>.</summary>
/// <remarks>
/// The jobs collection is mapped through its backing field. The aggregate exposes
/// <c>IReadOnlyList</c> and mutates only through <c>AddJob</c>, and Entity Framework must respect
/// that rather than reach for a setter the domain deliberately does not have.
/// </remarks>
public sealed class ConversionProjectConfiguration : IEntityTypeConfiguration<ConversionProject>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConversionProject> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Projects");
        builder.HasKey(project => project.Id);

        builder.Property(project => project.Id)
            .HasConversion(DomainValueConverters.ProjectId)
            .ValueGeneratedNever();

        builder.Property(project => project.Name).HasMaxLength(256).IsRequired();
        builder.Property(project => project.Description).HasMaxLength(2_048);
        builder.Property(project => project.CreatedAtUtc).IsRequired();
        builder.Property(project => project.ModifiedAtUtc);

        builder.Property(project => project.Settings)
            .HasConversion(DomainValueConverters.Settings)
            .HasColumnType("TEXT")
            .IsRequired();

        builder.HasIndex(project => project.Name).IsUnique();
        builder.HasIndex(project => project.ModifiedAtUtc);

        builder.Metadata
            .FindNavigation(nameof(ConversionProject.Jobs))!
            .SetPropertyAccessMode(PropertyAccessMode.Field);

        builder.HasMany(project => project.Jobs)
            .WithOne()
            .HasForeignKey(job => job.ProjectId)
            .OnDelete(DeleteBehavior.Cascade);

        // Domain events are raised in memory and dispatched by the unit of work. Persisting them
        // would make the table a queue, which it is not.
        builder.Ignore(project => project.DomainEvents);
    }
}

/// <summary>Maps <see cref="ConversionJob"/>.</summary>
public sealed class ConversionJobConfiguration : IEntityTypeConfiguration<ConversionJob>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ConversionJob> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("Jobs");
        builder.HasKey(job => job.Id);

        builder.Property(job => job.Id)
            .HasConversion(DomainValueConverters.JobId)
            .ValueGeneratedNever();

        builder.Property(job => job.ProjectId)
            .HasConversion(DomainValueConverters.ProjectId)
            .IsRequired();

        builder.Property(job => job.Source)
            .HasConversion(DomainValueConverters.SourceReference)
            .HasColumnType("TEXT")
            .IsRequired();

        builder.Property(job => job.Status).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(job => job.CreatedAtUtc).IsRequired();

        builder.Property(job => job.LatestRunId).HasConversion(DomainValueConverters.NullableRunId);

        builder.HasIndex(job => job.ProjectId);
        builder.HasIndex(job => job.Status);

        builder.Ignore(job => job.DomainEvents);
    }
}
