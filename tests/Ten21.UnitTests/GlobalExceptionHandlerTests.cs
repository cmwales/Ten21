using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Ten21.Api.ExceptionHandling;
using Ten21.Domain.Exceptions;
using Xunit;

namespace Ten21.UnitTests;

public class GlobalExceptionHandlerTests
{
    private class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "Ten21.Api.Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static async Task<(ProblemDetails Problem, JsonDocument Raw, int StatusCode)> HandleAsync(
        Exception exception, string environmentName = "Production")
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Response.Body = new MemoryStream();

        var handler = new GlobalExceptionHandler(
            new FakeHostEnvironment { EnvironmentName = environmentName },
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(httpContext, exception, CancellationToken.None);
        Assert.True(handled);

        httpContext.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(httpContext.Response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        var raw = JsonDocument.Parse(json);
        var problem = JsonSerializer.Deserialize<ProblemDetails>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        })!;

        return (problem, raw, httpContext.Response.StatusCode);
    }

    [Theory]
    [InlineData(typeof(NotFoundException), 404)]
    [InlineData(typeof(UnauthorizedException), 401)]
    [InlineData(typeof(ForbiddenException), 403)]
    [InlineData(typeof(ConflictException), 409)]
    [InlineData(typeof(DomainException), 422)]
    public async Task MapsEachSimpleExceptionType_ToItsDesignatedStatusCode(Type exceptionType, int expectedStatus)
    {
        var exception = (Exception)Activator.CreateInstance(exceptionType, "test message")!;

        var (problem, _, statusCode) = await HandleAsync(exception);

        Assert.Equal(expectedStatus, statusCode);
        Assert.Equal(expectedStatus, problem.Status);
    }

    [Fact]
    public async Task ValidationException_MapsTo400()
    {
        var exception = new ValidationException(new Dictionary<string, string[]> { ["Email"] = ["Required"] });

        var (problem, _, statusCode) = await HandleAsync(exception);

        Assert.Equal(400, statusCode);
        Assert.Equal(400, problem.Status);
    }

    [Fact]
    public async Task UnrecognizedException_MapsTo500()
    {
        var (problem, _, statusCode) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.Equal(500, statusCode);
        Assert.Equal(500, problem.Status);
    }

    [Fact]
    public async Task ValidationException_PopulatesErrorsExtension()
    {
        var exception = new ValidationException(new Dictionary<string, string[]>
        {
            ["Email"] = ["Required", "Must be a valid email address"],
        });

        var (_, raw, _) = await HandleAsync(exception);

        var errors = raw.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("Email", out var emailErrors));
        Assert.Equal(2, emailErrors.GetArrayLength());
    }

    [Fact]
    public async Task UnhandledException_MasksDetailOutsideDevelopment()
    {
        var (problem, _, _) = await HandleAsync(new InvalidOperationException("sensitive internal detail"), "Production");

        Assert.DoesNotContain("sensitive internal detail", problem.Detail);
    }

    [Fact]
    public async Task UnhandledException_ShowsFullDetailInDevelopment()
    {
        var (problem, _, _) = await HandleAsync(new InvalidOperationException("sensitive internal detail"), "Development");

        Assert.Contains("sensitive internal detail", problem.Detail);
    }

    [Fact]
    public async Task CustomExceptionMessage_IsNeverMasked_RegardlessOfEnvironment()
    {
        // Custom exception types carry an intentional, caller-safe message by
        // construction -- unlike the generic 500 case, there's nothing to mask here.
        var (problem, _, _) = await HandleAsync(new NotFoundException("Property abc123 was not found."), "Production");

        Assert.Equal("Property abc123 was not found.", problem.Detail);
    }

    [Fact]
    public async Task EveryResponse_IncludesATraceId()
    {
        var (_, raw, _) = await HandleAsync(new NotFoundException("not found"));

        Assert.True(raw.RootElement.TryGetProperty("traceId", out _));
    }
}
