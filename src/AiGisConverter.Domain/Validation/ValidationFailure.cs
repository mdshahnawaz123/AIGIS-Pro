namespace AiGisConverter.Domain.Validation;

/// <summary>
/// One broken invariant, attributed to the member that broke it.
/// </summary>
/// <param name="MemberName">The property or argument at fault.</param>
/// <param name="Message">What is wrong with it, phrased for a human.</param>
/// <param name="Code">Stable machine-readable code, for example <c>Project.NameRequired</c>.</param>
public sealed record ValidationFailure(string MemberName, string Message, string? Code = null)
{
    /// <inheritdoc />
    public override string ToString() =>
        string.IsNullOrEmpty(MemberName) ? Message : $"{MemberName}: {Message}";
}
