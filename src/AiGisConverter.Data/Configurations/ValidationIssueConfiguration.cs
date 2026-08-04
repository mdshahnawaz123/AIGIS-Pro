using AiGisConverter.Data.Converters;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.QaQc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AiGisConverter.Data.Configurations;

/// <summary>
/// Maps <see cref="ValidationIssue"/>.
/// </summary>
/// <remarks>
/// Findings are stored against their run rather than as part of a <c>ValidationReport</c>: the
/// report is a computed view over them, and materialising it as a row would duplicate counts that
/// can drift from the findings they summarise.
/// </remarks>
public sealed class ValidationIssueConfiguration : IEntityTypeConfiguration<ValidationIssue>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ValidationIssue> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ToTable("ValidationIssues");
        builder.HasKey(issue => issue.Id);

        builder.Property(issue => issue.Id)
            .HasConversion(DomainValueConverters.IssueId)
            .ValueGeneratedNever();

        // The run the finding belongs to. A shadow property, because the domain entity is a
        // statement about data and has no reason to carry a foreign key.
        builder.Property<Guid>("RunId").IsRequired();
        builder.HasIndex("RunId");

        builder.Property(issue => issue.Severity).HasConversion<string>().HasMaxLength(16).IsRequired();
        builder.Property(issue => issue.Category).HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(issue => issue.Code).HasMaxLength(128).IsRequired();
        builder.Property(issue => issue.Message).HasMaxLength(4_000).IsRequired();
        builder.Property(issue => issue.DetectedAtUtc).IsRequired();

        builder.Property(issue => issue.Layer)
            .HasConversion(DomainValueConverters.LayerName)
            .HasMaxLength(256);

        builder.Property(issue => issue.FeatureId).HasMaxLength(128);
        builder.Property(issue => issue.FieldName).HasMaxLength(128);
        builder.Property(issue => issue.LocationX);
        builder.Property(issue => issue.LocationY);
        builder.Property(issue => issue.Remediation).HasMaxLength(2_000);

        builder.Ignore(issue => issue.HasLocation);
        builder.Ignore(issue => issue.DomainEvents);

        builder.HasIndex(issue => issue.Severity);
        builder.HasIndex(issue => issue.Code);
    }
}
