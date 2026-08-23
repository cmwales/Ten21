using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// End-to-end proof of US-17's two-factor login gate, through the real HTTP pipeline.
/// Email-only (TOTP/authenticator-app support was built and then deliberately removed per
/// Founder decision -- see User_Stories_Phase_5.md). IEmailSender is substituted with a
/// fake (same pattern as EmailAuthEndToEndTests) so the mandatory-role email-OTP path can
/// be proven by extracting the actual code out of the actual email the controller sends,
/// not just asserting a 200.
///
/// Every test here bootstraps its own account via POST /api/auth/register, which always
/// yields PropertyManager (US-14) -- one of MandatoryTwoFactorRoles.Values, so EVERY login
/// in this file is 2FA-gated by construction; there's no "plain login" path left to
/// accidentally fall back to. Tests are also kept to AuthRateLimiterPolicy's 5-req/min-per-IP
/// budget (US-05/US-18) -- see the comment on each test that bends over backwards to stay
/// under it, same constraint EmailAuthEndToEndTests already documents.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class TwoFactorEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private FakeEmailSender _emailSender = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        Environment.SetEnvironmentVariable("ConnectionStrings__Ten21Database", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable(
            "Jwt__Key", "integration-test-only-signing-key-do-not-reuse-anywhere-else");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://api.ten21.io");
        Environment.SetEnvironmentVariable("Jwt__Audience", "https://app.ten21.io");
        Environment.SetEnvironmentVariable(
            "Turnstile__SecretKey", "1x0000000000000000000000000000000AA");
        Environment.SetEnvironmentVariable("Turnstile__AllowedHostnames", "example.com");

        _emailSender = new FakeEmailSender();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IEmailSender>(_ => _emailSender);
            });
        });

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Ten21DbContext>();
            await db.Database.MigrateAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            await RoleSeeder.SeedAsync(roleManager);
        }

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>register(1) + login(2) + verify-2fa(3) = 3 calls.</summary>
    [Fact]
    public async Task Login_SelfRegisteredPropertyManager_RequiresEmailCode_ThenVerifyIssuesFullSession()
    {
        var email = "mandatory-2fa@ten21.io";
        const string password = "Mandatory-2fa-Passw0rd!1";

        await RegisterAsync(email, password);
        _emailSender.SentEmails.Clear(); // discard the activation email -- not what this proves

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginData = await ReadDataAsync(loginResponse);
        Assert.True(loginData.GetProperty("requiresTwoFactor").GetBoolean());
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var sent = Assert.Single(_emailSender.SentEmails, e => e.Subject.Contains("Your Ten21 sign-in code"));
        var code = ExtractCode(sent.HtmlBody);

        var verifyResponse = await PostWithBearerAsync("/api/auth/login/verify-2fa", challengeToken, new { code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var verifyData = await ReadDataAsync(verifyResponse);
        Assert.Equal("PropertyManager", verifyData.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(verifyData.GetProperty("accessToken").GetString()));
    }

    /// <summary>register(1) + login(2) + verify-2fa-with-wrong-code(3) = 3 calls.</summary>
    [Fact]
    public async Task VerifyTwoFactor_WrongCode_IsRejected()
    {
        var email = "wrong-2fa-code@ten21.io";
        const string password = "Wrong-2fa-Code-Passw0rd!1";

        await RegisterAsync(email, password);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var loginData = await ReadDataAsync(loginResponse);
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var verifyResponse = await PostWithBearerAsync(
            "/api/auth/login/verify-2fa", challengeToken, new { code = "000000" });

        Assert.Equal(HttpStatusCode.Unauthorized, verifyResponse.StatusCode);
    }

    /// <summary>No auth call budget consumed by this one at all -- proves the endpoint
    /// itself requires *some* authenticated caller, not specifically a full session.</summary>
    [Fact]
    public async Task VerifyTwoFactor_WithNoChallengeToken_IsRejected()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task RegisterAsync(string email, string password)
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "TwoFactor",
            lastName = "Test",
            email,
            password,
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Two Factor Test Co",
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<HttpResponseMessage> PostWithBearerAsync(string url, string bearerToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Authorization", $"Bearer {bearerToken}");
        return await _client.SendAsync(request);
    }

    private static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static string ExtractCode(string htmlBody)
    {
        var match = Regex.Match(htmlBody, "<strong>([0-9]{6})</strong>");
        Assert.True(match.Success, $"No 6-digit code found in email body: {htmlBody}");
        return match.Groups[1].Value;
    }

    private class FakeEmailSender : IEmailSender
    {
        public List<(string ToEmail, string Subject, string HtmlBody)> SentEmails { get; } = [];

        public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
        {
            SentEmails.Add((toEmail, subject, htmlBody));
            return Task.CompletedTask;
        }
    }
}
