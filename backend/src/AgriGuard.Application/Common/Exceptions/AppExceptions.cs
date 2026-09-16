namespace AgriGuard.Application.Common.Exceptions;

/// <summary>
/// Base for expected, client-caused failures. Application services throw these; the API's global
/// exception handler maps each one to an RFC 7807 ProblemDetails response with a fixed status code.
/// Anything that is NOT an <see cref="AppException"/> is treated as a server fault (500).
/// </summary>
public abstract class AppException(string message) : Exception(message);

/// <summary>404 — the requested resource does not exist (or the caller may not know it exists).</summary>
public sealed class NotFoundException(string resource, object key)
    : AppException($"{resource} '{key}' was not found.");

/// <summary>403 — authenticated, but not allowed to touch this specific resource (e.g. another farmer's plot).</summary>
public sealed class ForbiddenAccessException(string message = "You do not have access to this resource.")
    : AppException(message);

/// <summary>409 — the request conflicts with current state (duplicate, stale version, already decided).</summary>
public sealed class ConflictException(string message) : AppException(message);

/// <summary>400 — request input failed validation. Keys are field names, values are messages.</summary>
public sealed class RequestValidationException(IDictionary<string, string[]> errors)
    : AppException("One or more validation errors occurred.")
{
    public IDictionary<string, string[]> Errors { get; } = errors;

    public RequestValidationException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] })
    {
    }
}

/// <summary>
/// 422 — the input is well-formed but violates a business rule (e.g. illegal crop-stage transition).
/// <paramref name="code"/> is a stable machine-readable identifier clients can switch on.
/// </summary>
public sealed class BusinessRuleException(string code, string message) : AppException(message)
{
    public string Code { get; } = code;
}
