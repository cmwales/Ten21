using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Ten21.Api.Contracts;

namespace Ten21.Api.Filters;

/// <summary>
/// Wraps every 2xx action result's payload in the standardized ApiResponse&lt;T&gt; envelope
/// (US-08), so individual controllers never need to remember to do this -- consistency is
/// structural, not a coding convention someone can forget on a new endpoint.
///
/// Deliberately skips: results with no Value (e.g. NoContentResult -- 204 has nothing to
/// wrap), non-ObjectResult results (e.g. FileResult -- raw binary payloads shouldn't be
/// JSON-wrapped), and anything already an ApiResponse&lt;T&gt; (defensive, avoids double-wrapping
/// if a controller ever constructs one explicitly). Error responses are untouched here
/// entirely -- those flow through US-09's RFC 7807 ProblemDetails middleware instead, which
/// has its own distinct shape and is never expected to pass through this filter at all.
/// </summary>
public class ApiResponseWrappingFilter : IAsyncResultFilter
{
    public async Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is ObjectResult objectResult
            && objectResult.Value is not null
            && objectResult.StatusCode is null or (>= 200 and < 300)
            && !ApiResponseFactory.IsAlreadyWrapped(objectResult.Value))
        {
            var statusCode = objectResult.StatusCode ?? StatusCodes.Status200OK;
            var wrapped = ApiResponseFactory.Wrap(objectResult.Value, statusCode, context.HttpContext.TraceIdentifier);
            objectResult.Value = wrapped;
        }

        await next();
    }
}
