using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
/// End-to-end proof of US-15's Google Sign-In flow through the real HTTP pipeline. A real
/// Google-signed ID token can't be fabricated in a test, so IGoogleIdTokenVerifier is
/// substituted with a fake via WithWebHostBuilder's ConfigureTestServices -- everything
/// downstream of that seam (user creation/linking, interim tokens, complete-profile,
/// workspace provisioning) is the real production code path, real Postgres included.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class GoogleAuthEndToEndTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private FakeGoogleIdTokenVerifier _googleVerifier = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        // Same env-var-before-Build() reasoning as AuthEndToEndTests -- see its comment.
        Environment.SetEnvironmentVariable("ConnectionStrings__Ten21Database", _postgres.GetConnectionString());
        Environment.SetEnvironmentVariable(
            "Jwt__Key", "integration-test-only-signing-key-do-not-reuse-anywhere-else");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "https://api.ten21.io");
        Environment.SetEnvironmentVariable("Jwt__Audience", "https://app.ten21.io");
        Environment.SetEnvironmentVariable(
            "Turnstile__SecretKey", "1x0000000000000000000000000000000AA");
        Environment.SetEnvironmentVariable("Turnstile__AllowedHostnames", "example.com");

        _googleVerifier = new FakeGoogleIdTokenVerifier();

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");
            builder.ConfigureTestServices(services =>
            {
                services.AddScoped<IGoogleIdTokenVerifier>(_ => _googleVerifier);
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
    public async Task GoogleLogin_FirstTimeSignup_RequiresProfileCompletion_ThenIssuesFullAuthResponse()
    {
        _googleVerifier.NextResult = new GoogleIdentity(
            Subject: "google-subject-123",
            Email: "new-google-user@ten21.io",
            EmailVerified: true,
            GivenName: "Gina",
            FamilyName: "Oogle");

        var googleResponse = await _client.PostAsJsonAsync("/api/auth/google", new { idToken = "fake-id-token" });
        Assert.Equal(HttpStatusCode.OK, googleResponse.StatusCode);

        var googleJson = await googleResponse.Content.ReadAsStringAsync();
        using var googleDoc = JsonDocument.Parse(googleJson);
        var googleData = googleDoc.RootElement.GetProperty("data");

        Assert.True(googleData.GetProperty("requiresProfileCompletion").GetBoolean());
        var interimToken = googleData.GetProperty("interimToken").GetString();
        Assert.False(string.IsNullOrEmpty(interimToken));

        // The interim token carries no tenant_id/role -- confirm it's rejected by an
        // ordinary authenticated endpoint that needs a real tenant context.
        var meRequest = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meRequest.Headers.Add("Authorization", $"Bearer {interimToken}");
        var meResponse = await _client.SendAsync(meRequest);
        Assert.Equal(HttpStatusCode.OK, meResponse.StatusCode); // /me only needs *any* authenticated caller
        var meJson = await meResponse.Content.ReadAsStringAsync();
        using var meDoc = JsonDocument.Parse(meJson);
        // No tenant_id claim was ever set, so /me's tenantId comes back null.
        Assert.Equal(JsonValueKind.Null, meDoc.RootElement.GetProperty("data").GetProperty("tenantId").ValueKind);

        var completeRequest = new HttpRequestMessage(HttpMethod.Post, "/api/auth/complete-profile")
        {
            Content = JsonContent.Create(new
            {
                phoneNumber = "555-0100",
                address = "1 Google Way",
                workspaceName = "Gina's Properties",
                portfolioSize = 2,
            }),
        };
        completeRequest.Headers.Add("Authorization", $"Bearer {interimToken}");
        var completeResponse = await _client.SendAsync(completeRequest);

        Assert.Equal(HttpStatusCode.OK, completeResponse.StatusCode);
        var completeJson = await completeResponse.Content.ReadAsStringAsync();
        using var completeDoc = JsonDocument.Parse(completeJson);
        var completeData = completeDoc.RootElement.GetProperty("data");

        Assert.Equal("PropertyManager", completeData.GetProperty("role").GetString());
        Assert.NotEqual(Guid.Empty, completeData.GetProperty("tenantId").GetGuid());
    }

    [Fact]
    public async Task GoogleLogin_ExistingAccountWithWorkspace_ReturnsFullAuthResponseDirectly()
    {
        // Register a normal account first (real Turnstile test secret + real registration
        // path), then simulate that SAME email signing in via Google for the first time.
        var email = "already-registered@ten21.io";
        var registerResponse = await _client.PostAsJsonAsync("/api/auth/register", new
        {
            firstName = "Already",
            lastName = "Registered",
            email,
            password = "Already-Registered-Passw0rd!1",
            phoneNumber = (string?)null,
            address = (string?)null,
            workspaceName = "Already Registered Co",
            portfolioSize = 1,
            agreedToTerms = true,
            turnstileToken = "test-token",
        });
        Assert.Equal(HttpStatusCode.OK, registerResponse.StatusCode);

        _googleVerifier.NextResult = new GoogleIdentity(
            Subject: "google-subject-for-existing-user",
            Email: email,
            EmailVerified: true,
            GivenName: "Already",
            FamilyName: "Registered");

        var googleResponse = await _client.PostAsJsonAsync("/api/auth/google", new { idToken = "fake-id-token" });
        Assert.Equal(HttpStatusCode.OK, googleResponse.StatusCode);

        var json = await googleResponse.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(json);
        var data = document.RootElement.GetProperty("data");

        // Auto-linked, full session issued immediately -- no profile-completion detour for
        // an account that already has a workspace.
        Assert.False(data.TryGetProperty("requiresProfileCompletion", out _));
        Assert.Equal("PropertyManager", data.GetProperty("role").GetString());
    }

    [Fact]
    public async Task GoogleLogin_UnverifiedEmail_IsRejected()
    {
        _googleVerifier.NextResult = new GoogleIdentity(
            Subject: "google-subject-unverified",
            Email: "unverified@ten21.io",
            EmailVerified: false,
            GivenName: "Un",
            FamilyName: "Verified");

        var response = await _client.PostAsJsonAsync("/api/auth/google", new { idToken = "fake-id-token" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GoogleLogin_InvalidToken_IsRejected()
    {
        _googleVerifier.NextResult = null; // simulates GoogleJsonWebSignature.ValidateAsync rejecting it

        var response = await _client.PostAsJsonAsync("/api/auth/google", new { idToken = "garbage" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CompleteProfile_CalledTwice_SecondCallConflicts()
    {
        _googleVerifier.NextResult = new GoogleIdentity(
            Subject: "google-subject-double-complete",
            Email: "double-complete@ten21.io",
            EmailVerified: true,
            GivenName: "Double",
            FamilyName: "Complete");

        var googleResponse = await _client.PostAsJsonAsync("/api/auth/google", new { idToken = "fake-id-token" });
        var googleJson = await googleResponse.Content.ReadAsStringAsync();
        using var googleDoc = JsonDocument.Parse(googleJson);
        var interimToken = googleDoc.RootElement.GetProperty("data").GetProperty("interimToken").GetString();

        async Task<HttpResponseMessage> CompleteAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/complete-profile")
            {
                Content = JsonContent.Create(new
                {
                    phoneNumber = (string?)null,
                    address = (string?)null,
                    workspaceName = "Workspace",
                    portfolioSize = 1,
                }),
            };
            request.Headers.Add("Authorization", $"Bearer {interimToken}");
            return await _client.SendAsync(request);
        }

        var first = await CompleteAsync();
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await CompleteAsync();
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    private class FakeGoogleIdTokenVerifier : IGoogleIdTokenVerifier
    {
        public GoogleIdentity? NextResult { get; set; }

        public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken = default)
            => Task.FromResult(NextResult);
    }
}
