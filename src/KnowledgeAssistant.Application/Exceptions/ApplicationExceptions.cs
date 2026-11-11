namespace KnowledgeAssistant.Application.Exceptions;

/// <summary>
/// The requested resource does not exist <em>or the caller may not see it</em>. The two cases are
/// deliberately indistinguishable so existence does not leak across tenants.
/// </summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>The request conflicts with existing state (for example, duplicate content).</summary>
public sealed class ConflictException(string message) : Exception(message);

/// <summary>The request is well-formed but violates a business rule that field-level validation cannot express.</summary>
public sealed class RequestValidationException : Exception
{
    /// <summary>Creates the exception for a single invalid field.</summary>
    /// <param name="field">Name of the offending request field (camelCase, as in the JSON contract).</param>
    /// <param name="error">Human-readable reason.</param>
    public RequestValidationException(string field, string error)
        : base(error)
    {
        Errors = new Dictionary<string, string[]> { [field] = [error] };
    }

    /// <summary>Validation errors keyed by field name.</summary>
    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
