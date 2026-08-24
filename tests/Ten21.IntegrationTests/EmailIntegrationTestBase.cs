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
/// Shared WebApplicationFactory/Testcontainers/FakeEmailSender scaffolding, plus the
/// register/bearer-post/read-response-data/extract-2FA-code helpers every test class needing
/// a real Postgres-backed host with IEmailSender substituted for a fake ended up
/// re-implementing identically. EmailAuthEndToEndTests, TwoFactorEndToEndTests, and
/// PropertiesEndToEndTests all had byte-for-byte copies of this before it was extracted here
/// -- a code review specifically flagged the drift risk of three independent ~60-line copies
/// (a fix to the 2FA email subject line or the register payload shape had to be made in three
/// places in lockstep, with nothing enforcing they stayed consistent). AuthEndToEndTests (no
/// email substitution at all) and GoogleAuthEndToEndTests (a different fake -- Google token
/// verification, not email) are deliberately NOT based on this class; their setup is
/// genuinely different, not just superficially similar, so forcing them onto this base would
/// be the premature-abstraction mistake in the other direction.
/// Not itself decorated with [Collection] -- xUnit's [Collection] attribute must be applied
/// to the concrete test class for its DisableParallelization to actually apply, not just
/// inherited from an abstract base, so every subclass below declares it explicitly.
/// </summary>
public abstract class EmailIntegrationTestBase : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    protected WebApplicationFactory<Program> Factory { get; private set; } = null!;
    protected HttpClient Client { get; private set; } = null!;
    protected FakeEmailSender EmailSender { get; private set; } = null!;

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

        EmailSender = new FakeEmailSender();

        Factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IEmailSender>(_ => EmailSender);
            });
        });

        await using (var scope = Factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Ten21DbContext>();
            await db.Database.MigrateAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            await RoleSeeder.SeedAsync(roleManager);
        }

        Client = Factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        Client.Dispose();
        await Factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    protected async Task RegisterAsync(
        string email,
        string password,
        string firstName = "Test",
        string lastName = "User",
        string workspaceName = "Test Co")
    {
        var response = await Client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName,
            lastName,
            email,
            password,
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName,
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    protected async Task<HttpResponseMessage> PostWithBearerAsync(string url, string bearerToken, object body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("Authorization", $"Bearer {bearerToken}");
        return await Client.SendAsync(request);
    }

    protected static async Task<JsonElement> ReadDataAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").Clone();
    }

    protected static string ExtractCode(string htmlBody)
    {
        var match = Regex.Match(htmlBody, "<strong>([0-9]{6})</strong>");
        Assert.True(match.Success, $"No 6-digit code found in email body: {htmlBody}");
        return match.Groups[1].Value;
    }
}

public class FakeEmailSender : IEmailSender
{
    public List<(string ToEmail, string Subject, string HtmlBody)> SentEmails { get; } = [];

    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        SentEmails.Add((toEmail, subject, htmlBody));
        return Task.CompletedTask;
    }
}
