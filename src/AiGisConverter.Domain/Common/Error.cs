namespace AiGisConverter.Domain.Common;

/// <summary>
/// Represents a stable, machine-readable failure descriptor returned by <see cref="Result"/>.
/// </summary>
/// <param name="Code">Dot-separated stable identifier, for example <c>Ai.ProviderUnavailable</c>.</param>
/// <param name="Message">Human-readable description intended for logs and the user interface.</param>
public sealed record Error(string Code, string Message)
{
    /// <summary>Sentinel value used by successful results.</summary>
    public static readonly Error None = new(string.Empty, string.Empty);

    /// <summary>Creates an error describing an unexpected, unclassified failure.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <returns>A new <see cref="Error"/>.</returns>
    public static Error Unexpected(string message) => new("General.Unexpected", message);

    /// <summary>Creates an error describing an invalid argument or configuration value.</summary>
    /// <param name="message">Description of the failure.</param>
    /// <returns>A new <see cref="Error"/>.</returns>
    public static Error Validation(string message) => new("General.Validation", message);

    /// <inheritdoc />
    public override string ToString() => string.IsNullOrEmpty(Code) ? Message : $"{Code}: {Message}";
}
