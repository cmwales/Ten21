using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Ten21.Infrastructure.Identity;
using Ten21.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// End-to-end proof of US-02's refresh-token lifecycle, plus US-14's registration/workspace
/// provisioning, through the real HTTP pipeline: real Postgres (via Testcontainers), real
/// ASP.NET Core Identity, real HTTP-only cookies. This is the one thing
/// tests/Ten21.UnitTests can't exercise -- the actual AuthController actions wired to a
/// real database end to end, including cookie round-tripping through an HttpClient the way
/// a browser would.
/// </summary>
public class AuthEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Environment variables, not WithWebHostBuilder's ConfigureAppConfiguration: Program.cs
        // reads Jwt:Key/Issuer/Audience into local variables via builder.Configuration[...]
        // BEFORE builder.Build() runs (it needs them to configure JwtBearerOptions, itself
        // registered before Build()). ConfigureAppConfiguration only takes effect as part of
        // Build() itself, which is too late for a read that already happened -- confirmed by
        // this first coming back as a 500 "SymmetricSecurityKey, key length is zero" because
        // Jwt:Key was still resolving to appsettings.json's empty default. Environment
        // variables, by contrast, are folded in synchronously inside WebApplication.
        // CreateBuilder(args) itself, so they're already visible by the time Program.cs's
        // early reads happen, as long as they're set before the factory's lazy host init
        // fires (guaranteed here since nothing above this touches _factory.Services yet).
        // ConnectionStrings:Ten21Database doesn't strictly need this treatment (it's read
        // lazily inside AddDbContext's options delegate, which runs at first resolution, well
        // after Build()) but is set the same way for consistency.
        Environment.SetEnvironmentVariable("ConnectionStrings__Ten21Database", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable(
            "Jwt__Key", "integration-test-only-signing-key-do-not-reuse-anywhere-else");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://api.ten21.io");
        Environment.SetEnvironmentVariable("Jwt__Audience", "https://app.ten21.io");

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            // AuthController's refresh cookie is only non-Secure in Development (see its
            // SetRefreshTokenCookie/RefreshTokenCookie.Set comment) -- TestServer's default
            // client talks plain http://, so a Secure cookie would silently never round-trip
            // and every assertion below would fail with a misleading "no refresh token" 401.
            builder.UseEnvironment("Development");
        });

        // Migrate + seed roles explicitly here rather than relying on Program.cs's own
        // Development-gated bootstrap block to have run by the time .Services is touched --
        // RoleSeeder is idempotent, so this is safe even if that block also ran (it may or
        // may not, depending on WebApplicationFactory's host interception).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<Ten21DbContext>();
            await db.Database.MigrateAsync();

            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            await RoleSeeder.SeedAsync(roleManager);
        }

        // WebApplicationFactoryClientOptions.HandleCookies defaults to true, so the
        // ten21_refresh_token cookie /login sets is automatically resent by this same
        // HttpClient on the /refresh-token and /revoke-token calls below, same as a browser.
        _client = _factory.CreateClient();
    }

    /// <summary>
    /// DevSeeder is retired as of US-14 -- POST /api/auth/register (dogfooded here, not a
    /// direct DB insert) is now the real way any test in this class gets a usable account.
    /// </summary>
    private async Task RegisterTestUserAsync()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Test",
            lastName = "User",
            email = TestEmail,
            password = TestPassword,
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Test Workspace",
            portfolioSize = 3,
            agreedToTerms = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private const string TestEmail = "integration-test@ten21.io";
    private const string TestPassword = "Integration-Test-Passw0rd!1";

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task Login_Refresh_Revoke_ThenRefreshFails()
    {
        await RegisterTestUserAsync();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new
        {
            email = TestEmail,
            password = TestPassword,
        });

        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);
        var loginAccessToken = await ExtractAccessTokenAsync(loginResponse);
        Assert.False(string.IsNullOrEmpty(loginAccessToken));

        var refreshResponse = await _client.PostAsync("/api/auth/refresh-token", content: null);
        Assert.Equal(HttpStatusCode.OK, refreshResponse.StatusCode);
        var refreshedAccessToken = await ExtractAccessTokenAsync(refreshResponse);
        Assert.False(string.IsNullOrEmpty(refreshedAccessToken));
        Assert.NotEqual(loginAccessToken, refreshedAccessToken); // proves rotation actually happened

        var revokeResponse = await _client.PostAsync("/api/auth/revoke-token", content: null);
        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);

        // The cookie currently held is the one /refresh-token just rotated in; it was just
        // revoked by the call above, so a further refresh must now be rejected -- proves
        // revoke-token actually killed the live token rather than being a no-op.
        var refreshAfterRevoke = await _client.PostAsync("/api/auth/refresh-token", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, refreshAfterRevoke.StatusCode);
    }

    [Fact]
    public async Task Register_ProvisionsWorkspaceAndReturnsPropertyManagerAsPrimaryRole()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "New",
            lastName = "Landlord",
            email = "new-landlord@ten21.io",
            password = "Landlord-Passw0rd!1",
            phoneNumber = "555-0100",
            address = "123 Main St",
            workspaceName = "New Landlord Properties",
            portfolioSize = 5,
            agreedToTerms = true,
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");

        Assert.Equal("PropertyManager", data.GetProperty("role").GetString());
        Assert.False(string.IsNullOrEmpty(data.GetProperty("accessToken").GetString()));
        Assert.NotEqual(Guid.Empty, data.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsValidationProblem()
    {
        await RegisterTestUserAsync();

        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Duplicate",
            lastName = "User",
            email = TestEmail, // already registered above
            password = "Another-Passw0rd!1",
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Someone Else's Workspace",
            portfolioSize = 1,
            agreedToTerms = true,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithoutAgreeingToTerms_ReturnsValidationProblem()
    {
        var response = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "No",
            lastName = "Consent",
            email = "no-consent@ten21.io",
            password = "No-Consent-Passw0rd!1",
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Workspace",
            portfolioSize = 1,
            agreedToTerms = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static async Task<string?> ExtractAccessTokenAsync(HttpResponseMessage response)
    {
        var json = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        return document.RootElement.GetProperty("data").GetProperty("accessToken").GetString();
    }
}
