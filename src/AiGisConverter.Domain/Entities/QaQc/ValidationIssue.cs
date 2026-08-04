using AiGisConverter.Domain.Common;
using AiGisConverter.Domain.Enums;
using AiGisConverter.Domain.ValueObjects;

namespace AiGisConverter.Domain.Entities.QaQc;

/// <summary>
/// One validation finding. Immutable once raised.
/// </summary>
/// <remarks>
/// <para>
/// A finding is a statement about data at a moment in time, so nothing about it changes after it
/// is created. Suppressing or accepting a finding is recorded against the run, not by editing the
/// finding &#8212; otherwise the report would stop being a record of what was actually found.
/// </para>
/// <para>
/// The fluent <c>For…</c> and <c>At</c> methods return enriched copies that keep the same
/// identity, because adding context does not make it a different finding. Location is carried as a
/// bare coordinate rather than a geometry so a report can be rendered, and a map zoomed to the
/// problem, without reloading the dataset.
/// </para>
/// </remarks>
public sealed class ValidationIssue : Entity<ValidationIssueId>
{
    private ValidationIssue(
        ValidationIssueId id,
        IssueSeverity severity,
        IssueCategory category,
        string code,
        string message,
        DateTimeOffset detectedAtUtc,
        LayerName? layer,
        string? featureId,
        string? fieldName,
        double? locationX,
        double? locationY,
        string? remediation)
        : base(id)
    {
        Severity = severity;
        Category = category;
        Code = code;
        Message = message;
        DetectedAtUtc = detectedAtUtc;
        Layer = layer;
        FeatureId = featureId;
        FieldName = fieldName;
        LocationX = locationX;
        LocationY = locationY;
        Remediation = remediation;
    }

    /// <summary>Gets how serious the finding is.</summary>
    public IssueSeverity Severity { get; }

    /// <summary>Gets what kind of thing the finding is about.</summary>
    public IssueCategory Category { get; }

    /// <summary>Gets the stable rule code, for example <c>Geometry.SelfIntersection</c>.</summary>
    public string Code { get; }

    /// <summary>Gets the human-readable description.</summary>
    public string Message { get; }

    /// <summary>Gets the instant the finding was recorded.</summary>
    public DateTimeOffset DetectedAtUtc { get; }

    /// <summary>Gets the source layer concerned, when the finding is layer-specific.</summary>
    public LayerName? Layer { get; }

    /// <summary>Gets the feature concerned, when the finding is feature-specific.</summary>
    public string? FeatureId { get; }

    /// <summary>Gets the attribute field concerned, for attribute findings.</summary>
    public string? FieldName { get; }

    /// <summary>Gets the X of the location, when the finding has one.</summary>
    public double? LocationX { get; }

    /// <summary>Gets the Y of the location, when the finding has one.</summary>
    public double? LocationY { get; }

    /// <summary>Gets guidance on how to fix the problem, when the rule can offer any.</summary>
    public string? Remediation { get; }

    /// <summary>Gets a value indicating whether the finding carries a map location.</summary>
    public bool HasLocation => LocationX is not null && LocationY is not null;

    /// <summary>Creates a finding.</summary>
    /// <param name="severity">How serious it is.</param>
    /// <param name="category">What kind of thing it concerns.</param>
    /// <param name="code">The stable rule code.</param>
    /// <param name="message">The human-readable description.</param>
    /// <returns>The created finding.</returns>
    public static ValidationIssue Create(
        IssueSeverity severity,
        IssueCategory category,
        string code,
        string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        return new ValidationIssue(
            ValidationIssueId.New(),
            severity,
            category,
            code.Trim(),
            message.Trim(),
            DateTimeOffset.UtcNow,
            layer: null,
            featureId: null,
            fieldName: null,
            locationX: null,
            locationY: null,
            remediation: null);
    }

    /// <summary>Returns an enriched copy attributed to a layer.</summary>
    /// <param name="layer">The layer concerned.</param>
    /// <returns>A new finding with the same identity.</returns>
    public ValidationIssue ForLayer(LayerName layer)
    {
        ArgumentNullException.ThrowIfNull(layer);

        return With(layer: layer);
    }

    /// <summary>Returns an enriched copy attributed to a feature.</summary>
    /// <param name="featureId">The feature concerned.</param>
    /// <returns>A new finding with the same identity.</returns>
    public ValidationIssue ForFeature(string featureId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(featureId);

        return With(featureId: featureId);
    }

    /// <summary>Returns an enriched copy attributed to an attribute field.</summary>
    /// <param name="fieldName">The field concerned.</param>
    /// <returns>A new finding with the same identity.</returns>
    public ValidationIssue ForField(string fieldName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        return With(fieldName: fieldName);
    }

    /// <summary>Returns an enriched copy carrying a map location.</summary>
    /// <param name="x">Location X.</param>
    /// <param name="y">Location Y.</param>
    /// <returns>A new finding with the same identity.</returns>
    public ValidationIssue At(double x, double y) => With(locationX: x, locationY: y);

    /// <summary>Returns an enriched copy carrying remediation guidance.</summary>
    /// <param name="remediation">How to fix the problem.</param>
    /// <returns>A new finding with the same identity.</returns>
    public ValidationIssue WithRemediation(string remediation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(remediation);

        return With(remediation: remediation);
    }

    /// <inheritdoc />
    public override string ToString() => $"[{Severity}] {Code}: {Message}";

    private ValidationIssue With(
        LayerName? layer = null,
        string? featureId = null,
        string? fieldName = null,
        double? locationX = null,
        double? locationY = null,
        string? remediation = null) =>
        new(
            Id,
            Severity,
            Category,
            Code,
            Message,
            DetectedAtUtc,
            layer ?? Layer,
            featureId ?? FeatureId,
            fieldName ?? FieldName,
            locationX ?? LocationX,
            locationY ?? LocationY,
            remediation ?? Remediation);
}
