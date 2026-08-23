using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Application.Abstractions;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// End-to-end proof of US-16's activation/password-recovery flow through the real HTTP
/// pipeline. IEmailSender is substituted with a fake that captures every send (no real
/// SMTP in a test), so these tests extract the actual token out of the actual link the
/// controller builds -- proving the whole round trip, not just that each endpoint responds.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class EmailAuthEndToEndTests : IAsyncLifetime
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

    [Fact]
    public async Task Register_SendsActivationEmail_AndActivateConfirmsTheAccount()
    {
        var email = "activation-flow@ten21.io";
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Activation",
            lastName = "Flow",
            email,
            password = "Activation-Flow-Passw0rd!1",
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Activation Flow Co",
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var sent = Assert.Single(_emailSender.SentEmails);
        Assert.Equal(email, sent.ToEmail);
        Assert.Contains("Confirm your Ten21 account", sent.Subject);

        var (userId, token) = ExtractLinkParams(sent.HtmlBody, "/activate");

        var activateResponse = await _client.PostAsJsonAsync("/api/auth/activate", new { userId, token });
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
    }

    [Fact]
    public async Task Activate_GarbageToken_IsRejected()
    {
        var email = "garbage-token@ten21.io";
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Garbage",
            lastName = "Token",
            email,
            password = "Garbage-Token-Passw0rd!1",
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Garbage Token Co",
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var sent = Assert.Single(_emailSender.SentEmails);
        var (userId, _) = ExtractLinkParams(sent.HtmlBody, "/activate");

        var response = await _client.PostAsJsonAsync(
            "/api/auth/activate", new { userId, token = "this-is-not-a-real-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResendActivation_UnknownEmail_StillReturnsGenericAcknowledgement_AndSendsNothing()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/resend-activation", new { email = "no-such-account@ten21.io" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_emailSender.SentEmails);
    }

    /// <summary>
    /// Deliberately kept to exactly 5 /api/auth/* calls (register, forgot-password,
    /// reset-password, login-old-fails, login-new-succeeds) -- AuthRateLimiterPolicy's
    /// 5-req/min-per-IP limit (US-05/US-18) applies here too, and TestServer requests all
    /// share one IP-keyed bucket per test method. See RevokesActiveRefreshTokens below for
    /// why that proof needed its own separate test rather than being folded into this one.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_ThenResetPassword_AllowsLoginWithNewPasswordOnly()
    {
        const string originalPassword = "Original-Passw0rd!1";
        const string newPassword = "Brand-New-Passw0rd!1";
        var email = "reset-flow@ten21.io";

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Reset",
            lastName = "Flow",
            email,
            password = originalPassword,
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Reset Flow Co",
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        var forgotResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        var sent = Assert.Single(_emailSender.SentEmails, e => e.Subject.Contains("Reset your Ten21 password"));
        var (userId, token) = ExtractLinkParams(sent.HtmlBody, "/reset-password");

        var resetResponse = await _client.PostAsJsonAsync(
            "/api/auth/reset-password", new { userId, token, newPassword });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var loginWithOldPassword = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = originalPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithOldPassword.StatusCode);

        var loginWithNewPassword = await _client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = newPassword });
        Assert.Equal(HttpStatusCode.OK, loginWithNewPassword.StatusCode);
    }

    /// <summary>Same 5-calls-max constraint as above (register, refresh, forgot-password,
    /// reset-password, refresh) -- split into its own test rather than combined with the
    /// login-swap proof, which already uses its full budget on its own.</summary>
    [Fact]
    public async Task ResetPassword_RevokesAlreadyIssuedRefreshTokens()
    {
        var email = "revoke-on-reset@ten21.io";

        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Revoke",
            lastName = "OnReset",
            email,
            password = "Revoke-On-Reset-Passw0rd!1",
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Revoke On Reset Co",
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        // Register already left a live refresh-token cookie on _client; confirm it works
        // now so the post-reset check below actually proves something changed.
        var preResetRefresh = await _client.PostAsync("/api/auth/refresh-token", content: null);
        Assert.Equal(HttpStatusCode.OK, preResetRefresh.StatusCode);

        var forgotResponse = await _client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        var sent = Assert.Single(_emailSender.SentEmails, e => e.Subject.Contains("Reset your Ten21 password"));
        var (userId, token) = ExtractLinkParams(sent.HtmlBody, "/reset-password");

        var resetResponse = await _client.PostAsJsonAsync(
            "/api/auth/reset-password", new { userId, token, newPassword = "Brand-New-Passw0rd!1" });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        // The refresh cookie issued (and confirmed live) before the reset must now be dead.
        var postResetRefresh = await _client.PostAsync("/api/auth/refresh-token", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, postResetRefresh.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_StillReturnsGenericAcknowledgement_AndSendsNothing()
    {
        var response = await _client.PostAsJsonAsync(
            "/api/auth/forgot-password", new { email = "no-such-account@ten21.io" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_emailSender.SentEmails);
    }

    /// <summary>Pulls userId/token straight out of the href the controller actually built,
    /// rather than assuming a shape -- if the controller's link format ever changes, this
    /// test breaks loudly instead of silently testing the wrong thing.</summary>
    private static (string UserId, string Token) ExtractLinkParams(string htmlBody, string expectedPath)
    {
        var match = Regex.Match(htmlBody, "href=\"([^\"]+)\"");
        Assert.True(match.Success, $"No href found in email body: {htmlBody}");

        var uri = new Uri(match.Groups[1].Value);
        Assert.Contains(expectedPath, uri.AbsolutePath);

        var query = QueryHelpers.ParseQuery(uri.Query);
        var userId = query["userId"].ToString();
        var token = query["token"].ToString();
        return (userId, token);
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
