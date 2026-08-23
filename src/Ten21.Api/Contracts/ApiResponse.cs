namespace Ten21.Api.Contracts;

/// <summary>
/// The uniform success-response shape (US-08) every 2xx endpoint returns. Individual
/// controllers never construct this directly -- ApiResponseWrappingFilter applies it
/// automatically to every ObjectResult, so consistency is structural rather than a
/// convention someone has to remember. Error responses use a different, RFC 7807
/// ProblemDetails shape entirely (US-09) -- this envelope is success-only, matching the
/// acceptance criteria's own wording ("wrap output payloads").
/// </summary>
public record ApiResponse<T>(bool Success, T? Data, string? Message, int StatusCode, string TraceId);
