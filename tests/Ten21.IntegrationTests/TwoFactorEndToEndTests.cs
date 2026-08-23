using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
/// IEmailSender is substituted with a fake (same pattern as EmailAuthEndToEndTests) so the
/// mandatory-role email-OTP path can be proven by extracting the actual code out of the
/// actual email the controller sends, not just asserting a 200.
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
        Assert.Equal("Email", loginData.GetProperty("method").GetString());
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

    /// <summary>
    /// register(1) + login(2) = 2 calls. Regression test for a real gap this story's own
    /// test-writing surfaced: without EnsureFullSession, a caller holding only a
    /// 2fa-pending challenge token (obtainable with just the password, before ever proving
    /// the second factor) could disable TOTP outright -- defeating the entire point of
    /// requiring it.
    /// </summary>
    [Fact]
    public async Task DisableTotp_WithInterimChallengeToken_IsRejected()
    {
        var email = "disable-with-interim-token@ten21.io";
        const string password = "Disable-With-Interim-Passw0rd!1";

        await RegisterAsync(email, password);

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var challengeToken = (await ReadDataAsync(loginResponse)).GetProperty("challengeToken").GetString()!;

        var disableResponse = await PostWithBearerAsync("/api/auth/2fa/totp/disable", challengeToken, new { });

        Assert.Equal(HttpStatusCode.Forbidden, disableResponse.StatusCode);
    }

    /// <summary>register(1) + login(2) + verify-2fa(3) + totp/setup(4) + totp/enable(5) = 5
    /// calls -- right at the budget ceiling.</summary>
    [Fact]
    public async Task TotpSetup_ThenEnableWithValidCode_Succeeds()
    {
        var email = "totp-setup@ten21.io";
        const string password = "Totp-Setup-Passw0rd!1";

        var sessionToken = await RegisterLoginAndVerifyEmailOtpAsync(email, password);

        var setupResponse = await PostWithBearerAsync("/api/auth/2fa/totp/setup", sessionToken, new { });
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);
        var setupData = await ReadDataAsync(setupResponse);
        var sharedKey = setupData.GetProperty("sharedKey").GetString()!;
        Assert.StartsWith("otpauth://totp/", setupData.GetProperty("otpAuthUri").GetString());
        Assert.False(string.IsNullOrEmpty(sharedKey));

        // A real authenticator app would compute this from sharedKey itself; see
        // GenerateAuthenticatorCodeAsync's comment for why that can't be done via
        // UserManager directly.
        var validCode = await GenerateAuthenticatorCodeAsync(email);

        var enableResponse = await PostWithBearerAsync(
            "/api/auth/2fa/totp/enable", sessionToken, new { code = validCode });

        Assert.Equal(HttpStatusCode.OK, enableResponse.StatusCode);
    }

    /// <summary>register(1) + login(2) + verify-2fa(3) + totp/setup(4) + totp/enable-wrong-code(5)
    /// = 5 calls.</summary>
    [Fact]
    public async Task TotpEnable_WrongCode_IsRejected()
    {
        var email = "totp-wrong-code@ten21.io";
        const string password = "Totp-Wrong-Code-Passw0rd!1";

        var sessionToken = await RegisterLoginAndVerifyEmailOtpAsync(email, password);

        var setupResponse = await PostWithBearerAsync("/api/auth/2fa/totp/setup", sessionToken, new { });
        Assert.Equal(HttpStatusCode.OK, setupResponse.StatusCode);

        var enableResponse = await PostWithBearerAsync(
            "/api/auth/2fa/totp/enable", sessionToken, new { code = "000000" });

        Assert.Equal(HttpStatusCode.BadRequest, enableResponse.StatusCode);
    }

    /// <summary>
    /// TOTP enrollment itself is set up directly via UserManager (no HTTP call, no rate-limit
    /// budget spent) so the full budget is available for what this test actually proves:
    /// enabling switches the NEXT login's challenge method to Authenticator, and disabling
    /// switches it back to Email -- register(1) + login(2) + verify-authenticator(3) +
    /// totp/disable(4) + login-again(5) = 5 calls. The second login's own verify-2fa call
    /// is deliberately NOT made (that would be 6) -- asserting its `method` came back
    /// "Email" (and that a real code-bearing email was actually sent) is enough to prove
    /// disable changed the challenge, without needing to complete that second login too.
    /// </summary>
    [Fact]
    public async Task EnabledTotp_ChangesLoginChallengeMethod_DisablingRevertsToEmail()
    {
        var email = "totp-method-switch@ten21.io";
        const string password = "Totp-Method-Switch-Passw0rd!1";

        await RegisterAsync(email, password);
        _emailSender.SentEmails.Clear();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException();
            await userManager.ResetAuthenticatorKeyAsync(user);
            await userManager.SetTwoFactorEnabledAsync(user, true);
        }

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var loginData = await ReadDataAsync(loginResponse);
        Assert.Equal("Authenticator", loginData.GetProperty("method").GetString());
        Assert.Empty(_emailSender.SentEmails); // no email sent for an Authenticator challenge
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var code = await GenerateAuthenticatorCodeAsync(email);
        var verifyResponse = await PostWithBearerAsync("/api/auth/login/verify-2fa", challengeToken, new { code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);
        var sessionToken = (await ReadDataAsync(verifyResponse)).GetProperty("accessToken").GetString()!;

        var disableResponse = await PostWithBearerAsync("/api/auth/2fa/totp/disable", sessionToken, new { });
        Assert.Equal(HttpStatusCode.OK, disableResponse.StatusCode);

        var secondLoginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var secondLoginData = await ReadDataAsync(secondLoginResponse);
        // Still 2FA-gated (PropertyManager is mandatory regardless of TwoFactorEnabled) --
        // just back to email, since TOTP is now off.
        Assert.Equal("Email", secondLoginData.GetProperty("method").GetString());
        Assert.False(string.IsNullOrEmpty(secondLoginData.GetProperty("challengeToken").GetString()));

        // A real 6-digit code was actually emailed for this second challenge -- confirms
        // the switch back to Email is a real behavior change, not just a label.
        var sent = Assert.Single(_emailSender.SentEmails, e => e.Subject.Contains("Your Ten21 sign-in code"));
        Assert.Matches("[0-9]{6}", ExtractCode(sent.HtmlBody));
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

    /// <summary>register + login + verify-2fa via email OTP -- 3 calls, returns the resulting
    /// full-session access token for the caller's own remaining budget to spend.</summary>
    private async Task<string> RegisterLoginAndVerifyEmailOtpAsync(string email, string password)
    {
        await RegisterAsync(email, password);
        _emailSender.SentEmails.Clear();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var loginData = await ReadDataAsync(loginResponse);
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var sent = Assert.Single(_emailSender.SentEmails, e => e.Subject.Contains("Your Ten21 sign-in code"));
        var code = ExtractCode(sent.HtmlBody);

        var verifyResponse = await PostWithBearerAsync("/api/auth/login/verify-2fa", challengeToken, new { code });
        Assert.Equal(HttpStatusCode.OK, verifyResponse.StatusCode);

        return (await ReadDataAsync(verifyResponse)).GetProperty("accessToken").GetString()!;
    }

    /// <summary>
    /// Stands in for a physical authenticator app, which can't run in CI. NOT
    /// UserManager.GenerateTwoFactorTokenAsync(user, "Authenticator") -- confirmed by
    /// direct probe that AuthenticatorTokenProvider.GenerateAsync always returns "" (code
    /// *display* is the app's job; the server only ever validates, never generates one).
    /// This hand-rolled RFC 6238 TOTP (HMAC-SHA1, 30s step, 6 digits) against the real
    /// shared key is what a real app effectively computes, and is confirmed (by the same
    /// probe) to validate correctly against UserManager.VerifyTwoFactorTokenAsync.
    /// </summary>
    private async Task<string> GenerateAuthenticatorCodeAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByEmailAsync(email) ?? throw new InvalidOperationException();
        var key = await userManager.GetAuthenticatorKeyAsync(user)
            ?? throw new InvalidOperationException("No authenticator key set up yet -- call /2fa/totp/setup first.");
        return ComputeTotp(key);
    }

    private static string ComputeTotp(string base32Key)
    {
        var keyBytes = Base32Decode(base32Key);
        var timestep = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 30;
        var counter = BitConverter.GetBytes(timestep);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(counter);
        }

        using var hmac = new HMACSHA1(keyBytes);
        var hash = hmac.ComputeHash(counter);
        var offset = hash[^1] & 0xf;
        var binary =
            ((hash[offset] & 0x7f) << 24) |
            ((hash[offset + 1] & 0xff) << 16) |
            ((hash[offset + 2] & 0xff) << 8) |
            (hash[offset + 3] & 0xff);
        return (binary % 1_000_000).ToString("D6");
    }

    private static byte[] Base32Decode(string input)
    {
        const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
        input = input.TrimEnd('=');
        var bits = 0;
        var value = 0;
        var output = new List<byte>();
        foreach (var c in input)
        {
            value = (value << 5) | alphabet.IndexOf(char.ToUpperInvariant(c));
            bits += 5;
            if (bits >= 8)
            {
                output.Add((byte)((value >> (bits - 8)) & 0xff));
                bits -= 8;
            }
        }
        return output.ToArray();
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
