using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Ten21.Domain.Exceptions;

namespace Ten21.Api.ExceptionHandling;

/// <summary>
/// US-09: Global Exception Handling Engine & Error Taxonomy.
///
/// The single registered IExceptionHandler (see Program.cs) -- every unhandled exception
/// anywhere in the request pipeline, domain or otherwise, gets converted to RFC 7807
/// ProblemDetails here rather than leaking a raw stack trace to the client. Returning true
/// from TryHandleAsync tells ASP.NET Core "handled, don't run any further handlers or the
/// default developer exception page."
/// </summary>
public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly IHostEnvironment _environment;
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(IHostEnvironment environment, ILogger<GlobalExceptionHandler> logger)
    {
        _environment = environment;
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (statusCode, title) = MapException(exception);

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Type = $"https://httpstatuses.io/{statusCode}",
            Instance = httpContext.Request.Path,
        };

        problemDetails.Extensions["traceId"] = httpContext.TraceIdentifier;

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors;
        }

        if (statusCode == StatusCodes.Status500InternalServerError)
        {
            // Always logged with full detail server-side, regardless of environment --
            // only the CLIENT-facing response masks details outside Development.
            _logger.LogError(
                exception, "Unhandled exception processing {Method} {Path}",
                httpContext.Request.Method, httpContext.Request.Path);

            problemDetails.Detail = _environment.IsDevelopment()
                ? exception.ToString()
                : "An unexpected error occurred. Please contact support with the trace ID below.";
        }
        else
        {
            // Custom exception types (ValidationException, NotFoundException, etc.) carry
            // an intentional, caller-safe message by construction -- no masking needed.
            problemDetails.Detail = exception.Message;
        }

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);

        return true;
    }

    private static (int StatusCode, string Title) MapException(Exception exception) => exception switch
    {
        ValidationException => (StatusCodes.Status400BadRequest, "Validation Failed"),
        NotFoundException => (StatusCodes.Status404NotFound, "Not Found"),
        UnauthorizedException => (StatusCodes.Status401Unauthorized, "Unauthorized"),
        ForbiddenException => (StatusCodes.Status403Forbidden, "Forbidden"),
        ConflictException => (StatusCodes.Status409Conflict, "Conflict"),
        DomainException => (StatusCodes.Status422UnprocessableEntity, "Unprocessable Entity"),
        _ => (StatusCodes.Status500InternalServerError, "Internal Server Error"),
    };
}
