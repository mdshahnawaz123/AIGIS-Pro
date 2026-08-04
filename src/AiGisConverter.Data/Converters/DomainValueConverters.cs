using System.Text.Json;
using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Entities.Source;
using AiGisConverter.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AiGisConverter.Data.Converters;

/// <summary>
/// Maps the domain's strongly typed identifiers and value objects onto storable columns.
/// </summary>
/// <remarks>
/// <para>
/// Identifiers become <see cref="Guid"/> columns: the wrapper exists to stop a run identifier
/// being passed where a job identifier belongs, and that protection is a compile-time concern with
/// no reason to reach the database.
/// </para>
/// <para>
/// The composite value objects &#8212; settings, source references, coordinate systems &#8212;
/// become JSON text. They are read as a unit and never filtered on, so shredding them into columns
/// or owned entities would buy queryability nobody wants at the cost of a migration every time a
/// setting is added. Runs are queried by status and date; those stay real columns.
/// </para>
/// </remarks>
public static class DomainValueConverters
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
    };

    /// <summary>Converts a <see cref="ProjectId"/> to and from its underlying value.</summary>
    public static ValueConverter<ProjectId, Guid> ProjectId { get; } =
        new(id => id.Value, value => new ProjectId(value));

    /// <summary>Converts a <see cref="ConversionJobId"/> to and from its underlying value.</summary>
    public static ValueConverter<ConversionJobId, Guid> JobId { get; } =
        new(id => id.Value, value => new ConversionJobId(value));

    /// <summary>Converts a <see cref="ConversionRunId"/> to and from its underlying value.</summary>
    public static ValueConverter<ConversionRunId, Guid> RunId { get; } =
        new(id => id.Value, value => new ConversionRunId(value));

    /// <summary>Converts a nullable <see cref="ConversionRunId"/>.</summary>
    public static ValueConverter<ConversionRunId?, Guid?> NullableRunId { get; } =
        new(id => id == null ? null : id.Value.Value,
            value => value == null ? null : new ConversionRunId(value.Value));

    /// <summary>Converts a <see cref="ValidationIssueId"/> to and from its underlying value.</summary>
    public static ValueConverter<ValidationIssueId, Guid> IssueId { get; } =
        new(id => id.Value, value => new ValidationIssueId(value));

    /// <summary>Converts a <see cref="LayerName"/> to and from text.</summary>
    public static ValueConverter<LayerName?, string?> LayerName { get; } =
        new(name => name == null ? null : name.Value,
            value => value == null ? null : Domain.ValueObjects.LayerName.Create(value));

    /// <summary>Converts a <see cref="Confidence"/> to and from a double.</summary>
    public static ValueConverter<Confidence, double> Confidence { get; } =
        new(confidence => confidence.Value, value => Domain.ValueObjects.Confidence.Clamp(value));

    /// <summary>Converts a <see cref="CoordinateSystem"/> to and from JSON.</summary>
    public static ValueConverter<CoordinateSystem?, string?> CoordinateSystem { get; } =
        new(system => system == null ? null : JsonSerializer.Serialize(system, Json),
            value => value == null ? null : JsonSerializer.Deserialize<CoordinateSystem>(value, Json));

    /// <summary>Converts a <see cref="ConversionSettings"/> to and from JSON.</summary>
    public static ValueConverter<ConversionSettings, string> Settings { get; } =
        new(settings => JsonSerializer.Serialize(settings, Json),
            value => JsonSerializer.Deserialize<ConversionSettings>(value, Json)!);

    /// <summary>Converts a <see cref="SourceReference"/> to and from JSON.</summary>
    public static ValueConverter<SourceReference, string> SourceReference { get; } =
        new(reference => JsonSerializer.Serialize(new SourceReferenceRecord(
                reference.Location,
                reference.IsLiveSession,
                reference.Hints.ToDictionary(p => p.Key, p => p.Value)), Json),
            value => Rehydrate(value));

    /// <summary>Converts a mutable string list to and from JSON, for a backing field.</summary>
    public static ValueConverter<List<string>, string> StringList { get; } =
        new(list => JsonSerializer.Serialize(list, Json),
            value => JsonSerializer.Deserialize<List<string>>(value, Json) ?? new List<string>());

    /// <summary>
    /// Rebuilds a source reference from JSON.
    /// </summary>
    /// <remarks>
    /// Written as a static method rather than inline because the entity has no settable
    /// collection: the hints go back through <c>SetHint</c>, which an expression tree cannot do.
    /// </remarks>
    private static SourceReference Rehydrate(string json)
    {
        SourceReferenceRecord record = JsonSerializer.Deserialize<SourceReferenceRecord>(json, Json)
                                       ?? new SourceReferenceRecord(string.Empty, false, []);

        SourceReference reference = new(record.Location) { IsLiveSession = record.IsLiveSession };

        foreach (KeyValuePair<string, string> hint in record.Hints)
        {
            reference.SetHint(hint.Key, hint.Value);
        }

        return reference;
    }

    private sealed record SourceReferenceRecord(
        string Location,
        bool IsLiveSession,
        Dictionary<string, string> Hints);
}
