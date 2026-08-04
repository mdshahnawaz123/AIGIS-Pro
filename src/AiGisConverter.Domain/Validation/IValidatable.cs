namespace AiGisConverter.Domain.Validation;

/// <summary>
/// Implemented by types that can report whether their current state is internally consistent.
/// </summary>
/// <remarks>
/// This is for invariants an object cannot enforce in its constructor &#8212; typically because
/// the object is assembled in stages, such as a project that must have at least one job before it
/// can be queued. It is not a substitute for guard clauses, which remain the first line of defence.
/// </remarks>
public interface IValidatable
{
    /// <summary>Checks the object's invariants.</summary>
    /// <returns>The accumulated outcome. Never null.</returns>
    ValidationOutcome Validate();
}
