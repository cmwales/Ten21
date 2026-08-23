namespace Ten21.Api.Contracts;

/// <summary>
/// Constructs an ApiResponse&lt;T&gt; where T is only known at runtime (ApiResponseWrappingFilter
/// sees an ObjectResult.Value typed as plain `object`). Same reflection-based
/// generic-dispatch technique Ten21DbContext already uses for its query filters --
/// consistent style rather than a one-off.
/// </summary>
public static class ApiResponseFactory
{
    public static object Wrap(object data, int statusCode, string traceId, string? message = null)
    {
        var dataType = data.GetType();
        var responseType = typeof(ApiResponse<>).MakeGenericType(dataType);
        return Activator.CreateInstance(responseType, true, data, message, statusCode, traceId)!;
    }

    public static bool IsAlreadyWrapped(object value) =>
        value.GetType().IsGenericType && value.GetType().GetGenericTypeDefinition() == typeof(ApiResponse<>);
}
