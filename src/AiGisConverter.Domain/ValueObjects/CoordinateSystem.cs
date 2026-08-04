using System.Globalization;
using AiGisConverter.Domain.Exceptions;

namespace AiGisConverter.Domain.ValueObjects;

/// <summary>
/// A coordinate reference system, identified by authority and code.
/// </summary>
/// <remarks>
/// <para>
/// Immutable and compared by value: two <c>EPSG:27700</c> instances are the same system, which is
/// exactly the semantics wanted when checking whether a reprojection is needed.
/// </para>
/// <para>
/// The domain holds only the identity and, optionally, the WKT definition. It performs no
/// mathematics; transformation is the GIS layer's work, behind
/// <c>ICoordinateTransformer</c>. Keeping PROJ out of the domain is what allows the domain to be
/// unit-tested with no native dependencies.
/// </para>
/// </remarks>
public sealed record CoordinateSystem
{
    /// <summary>WGS 84 geographic coordinates, the default output system.</summary>
    public static readonly CoordinateSystem Wgs84 = new("EPSG", 4326, "WGS 84", isGeographic: true);

    /// <summary>WGS 84 / Pseudo-Mercator, as used by web tile services.</summary>
    public static readonly CoordinateSystem WebMercator =
        new("EPSG", 3857, "WGS 84 / Pseudo-Mercator", isGeographic: false);

    [System.Text.Json.Serialization.JsonConstructor]
    private CoordinateSystem(string authority, int code, string? name, bool isGeographic)
    {
        Authority = authority;
        Code = code;
        Name = name;
        IsGeographic = isGeographic;
    }

    /// <summary>Gets the registry that issued the code, normally <c>EPSG</c>.</summary>
    public string Authority { get; }

    /// <summary>Gets the numeric code within the authority.</summary>
    public int Code { get; }

    /// <summary>Gets the human-readable name, when known.</summary>
    public string? Name { get; init; }

    /// <summary>
    /// Gets a value indicating whether coordinates are angular (degrees) rather than linear.
    /// </summary>
    /// <remarks>
    /// Worth checking before applying a metric tolerance: a snapping tolerance of 0.001 means a
    /// millimetre in a projected system and roughly 111 metres in a geographic one.
    /// </remarks>
    public bool IsGeographic { get; init; }

    /// <summary>Gets the well-known text definition, when the source supplied one.</summary>
    public string? WellKnownText { get; init; }

    /// <summary>Gets the canonical identifier, for example <c>EPSG:4326</c>.</summary>
    public string Identifier => string.Create(
        CultureInfo.InvariantCulture,
        $"{Authority}:{Code}");

    /// <summary>Creates a system from an authority and code.</summary>
    /// <param name="authority">The issuing registry.</param>
    /// <param name="code">The numeric code.</param>
    /// <param name="name">Optional human-readable name.</param>
    /// <param name="isGeographic">Whether coordinates are angular.</param>
    /// <returns>The created system.</returns>
    /// <exception cref="InvalidCoordinateSystemException">The authority is blank or the code is not positive.</exception>
    public static CoordinateSystem Create(string authority, int code, string? name = null, bool isGeographic = false)
    {
        if (string.IsNullOrWhiteSpace(authority))
        {
            throw new InvalidCoordinateSystemException("A coordinate system authority is required.");
        }

        if (code <= 0)
        {
            throw new InvalidCoordinateSystemException(
                $"{authority}:{code}",
                "A coordinate system code must be a positive integer.");
        }

        return new CoordinateSystem(authority.Trim().ToUpperInvariant(), code, name, isGeographic);
    }

    /// <summary>Parses an identifier such as <c>EPSG:27700</c>, or a bare code such as <c>27700</c>.</summary>
    /// <param name="identifier">The identifier to parse.</param>
    /// <returns>The parsed system.</returns>
    /// <exception cref="InvalidCoordinateSystemException">The identifier is not recognised.</exception>
    public static CoordinateSystem Parse(string identifier) =>
        TryParse(identifier, out CoordinateSystem? system)
            ? system!
            : throw new InvalidCoordinateSystemException(
                identifier,
                $"'{identifier}' is not a recognised coordinate system identifier. Expected a form such as 'EPSG:27700'.");

    /// <summary>Attempts to parse an identifier.</summary>
    /// <param name="identifier">The identifier to parse.</param>
    /// <param name="system">The parsed system, when successful.</param>
    /// <returns><see langword="true"/> when the identifier was recognised.</returns>
    public static bool TryParse(string? identifier, out CoordinateSystem? system)
    {
        system = null;

        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        string trimmed = identifier.Trim();
        int separator = trimmed.IndexOf(':', StringComparison.Ordinal);

        string authority = separator < 0 ? "EPSG" : trimmed[..separator].Trim();
        string codeText = separator < 0 ? trimmed : trimmed[(separator + 1)..].Trim();

        if (authority.Length == 0 ||
            !int.TryParse(codeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int code) ||
            code <= 0)
        {
            return false;
        }

        // EPSG geographic systems occupy 4000-4999; treating this as a hint rather than a fact,
        // because the authoritative answer requires the EPSG registry, which lives in the GIS layer.
        bool isGeographic = authority.Equals("EPSG", StringComparison.OrdinalIgnoreCase)
                            && code is >= 4000 and <= 4999;

        system = new CoordinateSystem(authority.ToUpperInvariant(), code, null, isGeographic);
        return true;
    }

    /// <summary>Returns the same system with a definition attached.</summary>
    /// <param name="wellKnownText">The WKT definition.</param>
    /// <returns>A new instance carrying the definition.</returns>
    public CoordinateSystem WithWellKnownText(string wellKnownText) =>
        this with { WellKnownText = wellKnownText };

    /// <inheritdoc />
    public override string ToString() => Name is null ? Identifier : $"{Identifier} ({Name})";

    /// <inheritdoc />
    public bool Equals(CoordinateSystem? other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return string.Equals(Authority, other.Authority, StringComparison.OrdinalIgnoreCase) && Code == other.Code;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        return HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(Authority), Code);
    }
}
