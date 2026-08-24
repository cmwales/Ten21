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
/// Regression coverage for a real reported bug: System.Text.Json's default enum handling
/// expects the numeric underlying value on the wire, not the string name -- so a request
/// body like {"propertyType":"SingleFamily"}, exactly what the Angular frontend (and any
/// real client) sends, was silently rejected with 400 before Program.cs registered a
/// JsonStringEnumConverter. Every existing PropertiesController unit test called the
/// controller action directly, bypassing JSON model binding entirely, so this went
/// undetected through Sprint 3's whole test suite -- only a live browser test against a
/// real backend caught it. This file exercises the controller through the real HTTP/JSON
/// pipeline (WebApplicationFactory, no shortcuts) specifically so that gap can't reopen.
///
/// Registration always yields PropertyManager (US-14), which is 2FA-mandatory, so getting
/// an authenticated session here requires the full register -> login -> verify-2fa dance,
/// same as TwoFactorEndToEndTests. Kept under AuthRateLimiterPolicy's 5-req/min-per-IP
/// budget: register(1) + login(2) + verify-2fa(3) + create-property(4) = 4 calls.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class PropertiesEndToEndTests : IAsyncLifetime
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

    /// <summary>register(1) + login(2) + verify-2fa(3) + create-property(4) = 4 calls.</summary>
    [Fact]
    public async Task CreateProperty_WithStringEnumValuesOverRealJson_Succeeds()
    {
        var email = "properties-json-enum@ten21.io";
        const string password = "Properties-Json-Enum-Passw0rd!1";

        var accessToken = await RegisterLoginAndVerifyAsync(email, password);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/properties")
        {
            Content = JsonContent.Create(new
            {
                name = "Riverside Apartments - Suite A",
                propertyType = "SingleFamily",
                streetAddress1 = "100 Main St",
                streetAddress2 = (string?)null,
                city = "Provo",
                state = "UT",
                postalCode = "84601",
                country = "USA",
                unitIdentifier = "Suite A",
                targetRent = 1200m,
                occupancyStatus = "Vacant",
            }),
        };
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await _client.SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created,
            $"Expected success, got {response.StatusCode}: {body}");

        using var document = JsonDocument.Parse(body);
        var data = document.RootElement.GetProperty("data");
        Assert.Equal("SingleFamily", data.GetProperty("propertyType").GetString());
        Assert.Equal("Vacant", data.GetProperty("occupancyStatus").GetString());
        Assert.Equal("Suite A", data.GetProperty("unitIdentifier").GetString());
    }

    private async Task<string> RegisterLoginAndVerifyAsync(string email, string password)
    {
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Properties",
            lastName = "Test",
            email,
            password,
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Properties Json Enum Test Co",
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);
        _emailSender.SentEmails.Clear();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginData = await ReadDataAsync(loginResponse);
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var sent = Assert.Single(_emailSender.SentEmails, e => e.Subject.Contains("Your Ten21 sign-in code"));
        var code = ExtractCode(sent.HtmlBody);

        var verifyRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login/verify-2fa")
        {
            Content = JsonContent.Create(new { code }),
        };
        verifyRequest.Headers.Add("Authorization", $"Bearer {challengeToken}");
        var verifyResponse = await _client.SendAsync(verifyRequest);
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        var verifyData = await ReadDataAsync(verifyResponse);
        return verifyData.GetProperty("accessToken").GetString()!;
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
