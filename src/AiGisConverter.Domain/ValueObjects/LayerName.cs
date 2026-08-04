namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// A source layer name, compared case-insensitively.
/// </summary>
/// <remarks>
/// CAD layer names are case-insensitive in practice but are stored with whatever case the author
/// typed, and the same standard appears as <c>C-STRM-PIPE</c> in one drawing and
/// <c>c-strm-pipe</c> in the next. Wrapping the name makes that equality rule a property of the
/// type rather than something every comparison site has to remember.
/// </remarks>
public sealed record LayerName
{
    private LayerName(string value) => Value = value;

    /// <summary>Gets the name as it appeared in the source.</summary>
    public string Value { get; }

    /// <summary>Gets the upper-case form used for comparison and grouping.</summary>
    public string Normalised => Value.ToUpperInvariant();

    /// <summary>Creates a layer name.</summary>
    /// <param name="value">The name as it appears in the source.</param>
    /// <returns>The created name.</returns>
    /// <exception cref="ArgumentException">The name is null, empty or whitespace.</exception>
    public static LayerName Create(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        return new LayerName(value.Trim());
    }

    /// <summary>Attempts to create a layer name.</summary>
    /// <param name="value">The candidate name.</param>
    /// <param name="name">The created name, when valid.</param>
    /// <returns><see langword="true"/> when the name was usable.</returns>
    public static bool TryCreate(string? value, out LayerName? name)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            name = null;
            return false;
        }

        name = new LayerName(value.Trim());
        return true;
    }

    /// <inheritdoc />
    public bool Equals(LayerName? other) =>
        other is not null && string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Value);

    /// <inheritdoc />
    public override string ToString() => Value;
}
