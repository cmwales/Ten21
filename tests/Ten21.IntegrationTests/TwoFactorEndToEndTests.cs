using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// End-to-end proof of US-17's two-factor login gate, through the real HTTP pipeline.
/// Email-only (TOTP/authenticator-app support was built and then deliberately removed per
/// Founder decision -- see User_Stories_Phase_5.md). IEmailSender is substituted with a fake
/// (see EmailIntegrationTestBase) so the mandatory-role email-OTP path can be proven by
/// extracting the actual code out of the actual email the controller sends, not just
/// asserting a 200.
///
/// Every test here bootstraps its own account via RegisterAsync, which always yields
/// PropertyManager (US-14) -- one of MandatoryTwoFactorRoles.Values, so EVERY login in this
/// file is 2FA-gated by construction; there's no "plain login" path left to accidentally
/// fall back to. Tests are also kept to AuthRateLimiterPolicy's 5-req/min-per-IP budget
/// (US-05/US-18) -- see the comment on each test that bends over backwards to stay under it,
/// same constraint EmailAuthEndToEndTests already documents.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class TwoFactorEndToEndTests : EmailIntegrationTestBase
{
    /// <summary>register(1) + login(2) + verify-2fa(3) = 3 calls.</summary>
    [Fact]
    public async Task Login_SelfRegisteredPropertyManager_RequiresEmailCode_ThenVerifyIssuesFullSession()
    {
        var email = "mandatory-2fa@ten21.io";
        const string password = "Mandatory-2fa-Passw0rd!1";

        await RegisterAsync(email, password, firstName: "TwoFactor", lastName: "Test", workspaceName: "Two Factor Test Co");
        EmailSender.SentEmails.Clear(); // discard the activation email -- not what this proves

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        Assert.Equal(HttpStatusCode.OK, loginResponse.StatusCode);

        var loginData = await ReadDataAsync(loginResponse);
        Assert.True(loginData.GetProperty("requiresTwoFactor").GetBoolean());
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var sent = Assert.Single(EmailSender.SentEmails, e => e.Subject.Contains("Your Ten21 sign-in code"));
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

        await RegisterAsync(email, password, firstName: "TwoFactor", lastName: "Test", workspaceName: "Two Factor Test Co");

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
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
        var response = await Client.PostAsJsonAsync("/api/auth/login/verify-2fa", new { code = "123456" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Regression test for a real reported bug: ASP.NET Core Identity's built-in Email
    /// token provider (TokenOptions.DefaultEmailProvider) is TOTP-based with a hardcoded,
    /// non-configurable ~3-minute step, so calling Login twice within that step returned
    /// the IDENTICAL code -- indistinguishable, from a user's perspective, from "requesting
    /// a new code did nothing." AuthController now generates its own cryptographically
    /// random code on every Login call, independent of any time step.
    /// register(1) + login(2) + login(3) = 3 calls.
    /// </summary>
    [Fact]
    public async Task Login_CalledTwiceInQuickSuccession_IssuesADifferentCodeEachTime()
    {
        var email = "repeat-login-2fa@ten21.io";
        const string password = "Repeat-Login-2fa-Passw0rd!1";

        await RegisterAsync(email, password, firstName: "TwoFactor", lastName: "Test", workspaceName: "Two Factor Test Co");
        EmailSender.SentEmails.Clear();

        await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        await Client.PostAsJsonAsync("/api/auth/login", new { email, password });

        Assert.Equal(2, EmailSender.SentEmails.Count);
        var firstCode = ExtractCode(EmailSender.SentEmails[0].HtmlBody);
        var secondCode = ExtractCode(EmailSender.SentEmails[1].HtmlBody);
        Assert.NotEqual(firstCode, secondCode);
    }

    /// <summary>
    /// Regression test for the same reported bug: the code's real validity window is a
    /// fixed 5 minutes, set explicitly by AuthController (not delegated to Identity's
    /// opaque, hardcoded TOTP step). Read directly off the challenge token's own code_exp
    /// claim rather than waiting 5 real minutes for an expiry to actually elapse.
    /// register(1) + login(2) = 2 calls.
    /// </summary>
    [Fact]
    public async Task Login_MandatoryTwoFactorRole_ChallengeTokenCarriesAFiveMinuteCodeExpiry()
    {
        var email = "code-expiry-2fa@ten21.io";
        const string password = "Code-Expiry-2fa-Passw0rd!1";

        await RegisterAsync(email, password, firstName: "TwoFactor", lastName: "Test", workspaceName: "Two Factor Test Co");

        var loginResponse = await Client.PostAsJsonAsync("/api/auth/login", new { email, password });
        var loginData = await ReadDataAsync(loginResponse);
        var challengeToken = loginData.GetProperty("challengeToken").GetString()!;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(challengeToken);
        var codeExpClaim = jwt.Claims.Single(c => c.Type == "code_exp").Value;
        var codeExpiresAtUtc = DateTimeOffset.FromUnixTimeSeconds(long.Parse(codeExpClaim));

        var expectedExpiry = DateTimeOffset.UtcNow.AddMinutes(5);
        Assert.True(
            Math.Abs((codeExpiresAtUtc - expectedExpiry).TotalSeconds) < 30,
            $"Expected code_exp near {expectedExpiry:o}, got {codeExpiresAtUtc:o}.");
    }
}
