namespace AiGisConverter.Domain.Common;

/// <summary>Identifies a <see cref="Entities.Project.ConversionProject"/>.</summary>
/// <param name="Value">The underlying value.</param>
/// <remarks>
/// Identifiers are wrapped rather than passed as bare <see cref="Guid"/> values so that handing a
/// run identifier to a method expecting a job identifier is a compile error rather than a support
/// ticket about the wrong record being loaded.
/// </remarks>
public readonly record struct ProjectId(Guid Value)
{
    /// <summary>Creates a new identifier.</summary>
    /// <returns>A new <see cref="ProjectId"/>.</returns>
    public static ProjectId New() => new(Guid.NewGuid());

    /// <summary>Gets the unassigned identifier.</summary>
    public static ProjectId Empty => new(Guid.Empty);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies a <see cref="Entities.Project.ConversionJob"/>.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct ConversionJobId(Guid Value)
{
    /// <summary>Creates a new identifier.</summary>
    /// <returns>A new <see cref="ConversionJobId"/>.</returns>
    public static ConversionJobId New() => new(Guid.NewGuid());

    /// <summary>Gets the unassigned identifier.</summary>
    public static ConversionJobId Empty => new(Guid.Empty);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies a <see cref="Entities.Project.ConversionRun"/>.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct ConversionRunId(Guid Value)
{
    /// <summary>Creates a new identifier.</summary>
    /// <returns>A new <see cref="ConversionRunId"/>.</returns>
    public static ConversionRunId New() => new(Guid.NewGuid());

    /// <summary>Gets the unassigned identifier.</summary>
    public static ConversionRunId Empty => new(Guid.Empty);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}

/// <summary>Identifies a <see cref="Entities.QaQc.ValidationIssue"/>.</summary>
/// <param name="Value">The underlying value.</param>
public readonly record struct ValidationIssueId(Guid Value)
{
    /// <summary>Creates a new identifier.</summary>
    /// <returns>A new <see cref="ValidationIssueId"/>.</returns>
    public static ValidationIssueId New() => new(Guid.NewGuid());

    /// <summary>Gets the unassigned identifier.</summary>
    public static ValidationIssueId Empty => new(Guid.Empty);

    /// <inheritdoc />
    public override string ToString() => Value.ToString("D");
}
