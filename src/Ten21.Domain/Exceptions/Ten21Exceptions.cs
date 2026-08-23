namespace Ten21.Domain.Exceptions;

/// <summary>
/// The custom exception taxonomy for US-09. Plain Exception subclasses using only BCL
/// types -- Domain stays framework-free even here. Each type maps to exactly one HTTP
/// status code in Api.ExceptionHandling.GlobalExceptionHandler; that mapping lives in Api
/// (not here) since "which status code" is a Web-layer/transport concern, while "this kind
/// of thing went wrong" is a Domain concern. Throwing these from Application/Infrastructure/
/// Api code is fine -- none of them require a reference back to Api.
/// </summary>

/// <summary>A business rule was violated. Maps to 422 Unprocessable Entity.</summary>
public class DomainException : Exception
{
    public DomainException(string message) : base(message)
    {
    }
}

/// <summary>The requested resource doesn't exist (or isn't visible to the caller, which
/// looks identical from the outside -- see BOLA/IDOR notes in SECURITY.docx). Maps to 404.</summary>
public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

/// <summary>One or more field-level validation failures. Maps to 400, with Errors populated
/// into RFC 7807's Errors extension dictionary.</summary>
public class ValidationException : Exception
{
    public IReadOnlyDictionary<string, string[]> Errors { get; }

    public ValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.")
    {
        Errors = errors;
    }
}

/// <summary>The caller isn't authenticated at all. Maps to 401. In practice, most
/// authentication failures are already handled by the JWT Bearer middleware itself before
/// application code runs -- this exists for the rarer case of application code discovering
/// mid-request that credentials it was given don't hold up.</summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message)
    {
    }
}

/// <summary>The caller is authenticated but not allowed to do this specific thing. Maps to
/// 403. Most authorization failures go through ASP.NET Core's policy engine (US-03) and
/// never reach application code as an exception at all -- this is for authorization logic
/// that can only be evaluated after loading domain data (e.g. resource-based checks).</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message)
    {
    }
}

/// <summary>The request conflicts with the current state of the resource (e.g. a unique
/// constraint violation surfaced as a domain-meaningful error). Maps to 409.</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message)
    {
    }
}
