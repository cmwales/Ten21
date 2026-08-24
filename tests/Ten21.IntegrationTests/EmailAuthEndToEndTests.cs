using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.WebUtilities;
using Xunit;

namespace Ten21.IntegrationTests;

/// <summary>
/// End-to-end proof of US-16's activation/password-recovery flow through the real HTTP
/// pipeline. IEmailSender is substituted with a fake that captures every send (see
/// EmailIntegrationTestBase), so these tests extract the actual token out of the actual link
/// the controller builds -- proving the whole round trip, not just that each endpoint
/// responds.
/// </summary>
[Collection(SequentialWebApplicationFactoryCollection.Name)]
public class EmailAuthEndToEndTests : EmailIntegrationTestBase
{
    [Fact]
    public async Task Register_SendsActivationEmail_AndActivateConfirmsTheAccount()
    {
        var email = "activation-flow@ten21.io";
        await RegisterAsync(email, "Activation-Flow-Passw0rd!1", firstName: "Activation", lastName: "Flow", workspaceName: "Activation Flow Co");

        var sent = Assert.Single(EmailSender.SentEmails);
        Assert.Equal(email, sent.ToEmail);
        Assert.Contains("Confirm your Ten21 account", sent.Subject);

        var (userId, token) = ExtractLinkParams(sent.HtmlBody, "/activate");

        var activateResponse = await Client.PostAsJsonAsync("/api/auth/activate", new { userId, token });
        Assert.Equal(HttpStatusCode.OK, activateResponse.StatusCode);
    }

    [Fact]
    public async Task Activate_GarbageToken_IsRejected()
    {
        var email = "garbage-token@ten21.io";
        await RegisterAsync(email, "Garbage-Token-Passw0rd!1", firstName: "Garbage", lastName: "Token", workspaceName: "Garbage Token Co");

        var sent = Assert.Single(EmailSender.SentEmails);
        var (userId, _) = ExtractLinkParams(sent.HtmlBody, "/activate");

        var response = await Client.PostAsJsonAsync(
            "/api/auth/activate", new { userId, token = "this-is-not-a-real-token" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ResendActivation_UnknownEmail_StillReturnsGenericAcknowledgement_AndSendsNothing()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/resend-activation", new { email = "no-such-account@ten21.io" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(EmailSender.SentEmails);
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

        await RegisterAsync(email, originalPassword, firstName: "Reset", lastName: "Flow", workspaceName: "Reset Flow Co");

        var forgotResponse = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        var sent = Assert.Single(EmailSender.SentEmails, e => e.Subject.Contains("Reset your Ten21 password"));
        var (userId, token) = ExtractLinkParams(sent.HtmlBody, "/reset-password");

        var resetResponse = await Client.PostAsJsonAsync(
            "/api/auth/reset-password", new { userId, token, newPassword });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        var loginWithOldPassword = await Client.PostAsJsonAsync(
            "/api/auth/login", new { email, password = originalPassword });
        Assert.Equal(HttpStatusCode.Unauthorized, loginWithOldPassword.StatusCode);

        var loginWithNewPassword = await Client.PostAsJsonAsync(
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
        await RegisterAsync(email, "Revoke-On-Reset-Passw0rd!1", firstName: "Revoke", lastName: "OnReset", workspaceName: "Revoke On Reset Co");

        // Register already left a live refresh-token cookie on Client; confirm it works
        // now so the post-reset check below actually proves something changed.
        var preResetRefresh = await Client.PostAsync("/api/auth/refresh-token", content: null);
        Assert.Equal(HttpStatusCode.OK, preResetRefresh.StatusCode);

        var forgotResponse = await Client.PostAsJsonAsync("/api/auth/forgot-password", new { email });
        Assert.Equal(HttpStatusCode.OK, forgotResponse.StatusCode);

        var sent = Assert.Single(EmailSender.SentEmails, e => e.Subject.Contains("Reset your Ten21 password"));
        var (userId, token) = ExtractLinkParams(sent.HtmlBody, "/reset-password");

        var resetResponse = await Client.PostAsJsonAsync(
            "/api/auth/reset-password", new { userId, token, newPassword = "Brand-New-Passw0rd!1" });
        Assert.Equal(HttpStatusCode.OK, resetResponse.StatusCode);

        // The refresh cookie issued (and confirmed live) before the reset must now be dead.
        var postResetRefresh = await Client.PostAsync("/api/auth/refresh-token", content: null);
        Assert.Equal(HttpStatusCode.Unauthorized, postResetRefresh.StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_UnknownEmail_StillReturnsGenericAcknowledgement_AndSendsNothing()
    {
        var response = await Client.PostAsJsonAsync(
            "/api/auth/forgot-password", new { email = "no-such-account@ten21.io" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(EmailSender.SentEmails);
    }

    /// <summary>Pulls userId/token straight out of the href the controller actually built,
    /// rather than assuming a shape -- if the controller's link format ever changes, this
    /// test breaks loudly instead of silently testing the wrong thing.</summary>
    private static (string UserId, string Token) ExtractLinkParams(string htmlBody, string expectedPath)
    {
        var match = System.Text.RegularExpressions.Regex.Match(htmlBody, "href=\"([^\"]+)\"");
        Assert.True(match.Success, $"No href found in email body: {htmlBody}");

        var uri = new Uri(match.Groups[1].Value);
        Assert.Contains(expectedPath, uri.AbsolutePath);

        var query = QueryHelpers.ParseQuery(uri.Query);
        var userId = query["userId"].ToString();
        var token = query["token"].ToString();
        return (userId, token);
    }
}
