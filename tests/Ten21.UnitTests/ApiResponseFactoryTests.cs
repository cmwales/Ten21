using Ten21.Api.Contracts;
using Xunit;

namespace Ten21.UnitTests;

public class ApiResponseFactoryTests
{
    private record SamplePayload(string Name, int Count);

    [Fact]
    public void Wrap_ProducesASuccessfulApiResponse_WithMatchingGenericType()
    {
        var payload = new SamplePayload("test", 3);

        var wrapped = ApiResponseFactory.Wrap(payload, 200, "trace-123");

        var response = Assert.IsType<ApiResponse<SamplePayload>>(wrapped);
        Assert.True(response.Success);
        Assert.Equal(payload, response.Data);
        Assert.Equal(200, response.StatusCode);
        Assert.Equal("trace-123", response.TraceId);
        Assert.Null(response.Message);
    }

    [Fact]
    public void Wrap_CarriesTheOptionalMessage()
    {
        var wrapped = ApiResponseFactory.Wrap("hello", 201, "trace-456", "Created successfully.");

        var response = Assert.IsType<ApiResponse<string>>(wrapped);
        Assert.Equal("Created successfully.", response.Message);
    }

    [Fact]
    public void IsAlreadyWrapped_TrueForAnExistingApiResponse()
    {
        var existing = new ApiResponse<string>(true, "data", null, 200, "trace-789");

        Assert.True(ApiResponseFactory.IsAlreadyWrapped(existing));
    }

    [Fact]
    public void IsAlreadyWrapped_FalseForAPlainPayload()
    {
        Assert.False(ApiResponseFactory.IsAlreadyWrapped(new SamplePayload("x", 1)));
    }

    [Fact]
    public void Wrap_WorksWithCollectionPayloads()
    {
        var payload = new List<string> { "a", "b", "c" };

        var wrapped = ApiResponseFactory.Wrap(payload, 200, "trace-list");

        var response = Assert.IsType<ApiResponse<List<string>>>(wrapped);
        Assert.Equal(3, response.Data!.Count);
    }
}
