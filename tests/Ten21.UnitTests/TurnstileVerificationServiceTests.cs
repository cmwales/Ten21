using System.Net;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Ten21.Infrastructure.Security;
using Xunit;

namespace Ten21.UnitTests;

/// <summary>
/// Exercises the THREE-condition gate (success + action + hostname) against a fake
/// HttpMessageHandler standing in for Cloudflare's siteverify endpoint -- no real network
/// call, no dependency on a real secret key. The real-network proof (against Cloudflare's
/// own published testing secret) lives in Ten21.IntegrationTests.AuthEndToEndTests.
/// </summary>
public class TurnstileVerificationServiceTests
{
    private static TurnstileVerificationService CreateService(
        string responseJson, HttpStatusCode statusCode = HttpStatusCode.OK, string allowedHostnames = "app.ten21.io")
    {
        var handler = new FakeHttpMessageHandler(new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
        });
        var httpClient = new HttpClient(handler);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Turnstile:SecretKey"] = "fake-secret",
                ["Turnstile:AllowedHostnames"] = allowedHostnames,
            })
            .Build();

        return new TurnstileVerificationService(
            httpClient, configuration, NullLogger<TurnstileVerificationService>.Instance);
    }

    [Fact]
    public async Task VerifyAsync_SuccessActionAndHostnameAllMatch_ReturnsTrue()
    {
        var service = CreateService(
            """{"success":true,"action":"register","hostname":"app.ten21.io","error-codes":[]}""");

        var result = await service.VerifyAsync("some-token", "1.2.3.4");

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyAsync_CloudflareReportsFailure_ReturnsFalse()
    {
        var service = CreateService(
            """{"success":false,"error-codes":["invalid-input-response"]}""");

        var result = await service.VerifyAsync("some-token", "1.2.3.4");

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_WrongAction_ReturnsFalse()
    {
        var service = CreateService(
            """{"success":true,"action":"contact-form","hostname":"app.ten21.io"}""");

        var result = await service.VerifyAsync("some-token", "1.2.3.4");

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_HostnameNotAllowListed_ReturnsFalse()
    {
        var service = CreateService(
            """{"success":true,"action":"register","hostname":"evil.example"}""",
            allowedHostnames: "app.ten21.io");

        var result = await service.VerifyAsync("some-token", "1.2.3.4");

        Assert.False(result);
    }

    [Fact]
    public async Task VerifyAsync_MissingAction_StillPassesWhenSuccessAndHostnameMatch()
    {
        // Matches Cloudflare's own published "always passes" testing secret, which omits
        // Action entirely -- see AuthEndToEndTests for the real-network proof of this shape.
        var service = CreateService(
            """{"success":true,"hostname":"app.ten21.io"}""");

        var result = await service.VerifyAsync("some-token", "1.2.3.4");

        Assert.True(result);
    }

    [Fact]
    public async Task VerifyAsync_HttpErrorFromCloudflare_ReturnsFalse()
    {
        var service = CreateService("", statusCode: HttpStatusCode.InternalServerError);

        var result = await service.VerifyAsync("some-token", "1.2.3.4");

        Assert.False(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task VerifyAsync_BlankToken_ReturnsFalseWithoutCallingCloudflare(string blankToken)
    {
        var service = CreateService("""{"success":true,"action":"register","hostname":"app.ten21.io"}""");

        var result = await service.VerifyAsync(blankToken, "1.2.3.4");

        Assert.False(result);
    }

    [Fact]
    public void Constructor_ThrowsIfSecretKeyMissing()
    {
        var httpClient = new HttpClient(new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)));
        var configuration = new ConfigurationBuilder().Build(); // no SecretKey set

        Assert.Throws<InvalidOperationException>(
            () => new TurnstileVerificationService(
                httpClient, configuration, NullLogger<TurnstileVerificationService>.Instance));
    }

    private class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}
